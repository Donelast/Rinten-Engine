using System;

namespace Editor.TerrainEditor;

/// <summary>Brushes you can use</summary>
public class BrushList
{
	public Brush Selected { get; set; }
	public List<Brush> Brushes = new();

	public BrushList()
	{
		LoadAll();
	}

	public void LoadAll()
	{
		Brushes.Clear();

		//The three shapes anybody actually sculpts with: how much of the radius is flat before the edge starts falling away.
		Brushes.Add( Brush.Made( "Soft", 0.0f ) );
		Brushes.Add( Brush.Made( "Medium", 0.45f ) );
		Brushes.Add( Brush.Made( "Hard", 0.85f ) );

		//And whatever a project put beside them. Not available in unit test.
		if ( FileSystem.Content is not null )
		{
			foreach ( var filename in FileSystem.Content.FindFile( "materials/tools/terrain/brushes", "*.png" ) )
			{
				var brush = Brush.LoadFromFile( $"materials/tools/terrain/brushes/{filename}" );

				if ( brush is not null ) Brushes.Add( brush );
			}
		}

		Selected = Brushes.FirstOrDefault();
	}
}

public class Brush
{
	public string Name { get; private set; }
	public Texture Texture { get; private set; }
	public Pixmap Pixmap { get; private set; }

	//How wide the falloff below is, in samples.
	private const int Side = 128;

	//Per-sample brush weights kept on the CPU, since the height map is a managed array
	private float[] weights;

	/// <summary>How much of the brush lands here. <paramref name="x"/> and <paramref name="y"/> run from minus one to one across the brush, and anything outside that is not under it at all.</summary>
	public float Weight( float x, float y )
	{
		if ( weights is null ) return 0.0f;
		if ( x < -1.0f || x > 1.0f || y < -1.0f || y > 1.0f ) return 0.0f;

		var u = ( x * 0.5f + 0.5f ) * ( Side - 1 );
		var v = ( y * 0.5f + 0.5f ) * ( Side - 1 );

		//Between the four around it, so a brush stretched over a few hundred texels has no steps in its edge.
		var x0 = (int)MathF.Floor( u );
		var y0 = (int)MathF.Floor( v );
		var fx = u - x0;
		var fy = v - y0;

		var a = At( x0, y0 );
		var b = At( x0 + 1, y0 );
		var c = At( x0, y0 + 1 );
		var d = At( x0 + 1, y0 + 1 );

		return MathX.Lerp( MathX.Lerp( a, b, fx ), MathX.Lerp( c, d, fx ), fy );

		float At( int px, int py )
		{
			if ( px < 0 || py < 0 || px >= Side || py >= Side ) return 0.0f;

			return weights[( py * Side ) + px];
		}
	}

	//A round brush, flat out to hardness of its radius and falling smoothly to nothing at the edge.
	internal static Brush Made( string name, float hardness )
	{
		var brush = new Brush { Name = name };

		hardness = Math.Clamp( hardness, 0.0f, 0.99f );

		var weights = new float[Side * Side];
		var bytes = new byte[Side * Side * 4];

		for ( var y = 0; y < Side; y++ )
		{
			//The sample's own centre, from minus one to one.
			var v = ( ( y + 0.5f ) / Side * 2.0f ) - 1.0f;

			for ( var x = 0; x < Side; x++ )
			{
				var u = ( ( x + 0.5f ) / Side * 2.0f ) - 1.0f;

				var radius = MathF.Sqrt( ( u * u ) + ( v * v ) );

				//One inside the hard core, nought outside the circle, and a smooth step between - squared, which is what a brush edge looks like when it is not a cone.
				var fall = radius <= hardness
					? 1.0f
					: 1.0f - Math.Clamp( ( radius - hardness ) / ( 1.0f - hardness ), 0.0f, 1.0f );

				var weight = fall * fall * ( 3.0f - 2.0f * fall );

				if ( radius > 1.0f ) weight = 0.0f;

				var at = ( y * Side ) + x;

				weights[at] = weight;

				var level = (byte)Math.Clamp( (int)( weight * 255.0f + 0.5f ), 0, 255 );

				bytes[at * 4 + 0] = level;
				bytes[at * 4 + 1] = level;
				bytes[at * 4 + 2] = level;
				bytes[at * 4 + 3] = level;
			}
		}

		brush.weights = weights;
		brush.Finish( bytes );

		return brush;
	}

	//The picture and the thumbnail, from the levels worked out above.
	private void Finish( byte[] bytes )
	{
		if ( !Rinten.Application.IsHeadless )
		{
			Texture = Texture.Create( Side, Side, ImageFormat.LINEAR_RGBA8888 )
				.WithName( $"terrain_brush_{Name}" )
				.Finish();

			Texture?.Update( new ReadOnlySpan<byte>( bytes ) );
		}

		Pixmap = new Pixmap( Side, Side );
		Pixmap.UpdateFromPixels( bytes, Side, Side );
	}

	internal static Brush LoadFromFile( string filename )
	{
		var full = FileSystem.Content?.GetFullPath( filename );

		if ( string.IsNullOrWhiteSpace( full ) ) return null;

		var brush = new Brush
		{
			Name = System.IO.Path.GetFileNameWithoutExtension( filename ),
			Texture = Texture.Load( filename ),
			Pixmap = Pixmap.FromFile( full ),
		};

		//The falloff has to come off the picture as well, or the brush draws one shape and sculpts another.
		brush.weights = new float[Side * Side];

		if ( brush.Pixmap is not null )
		{
			for ( var y = 0; y < Side; y++ )
			{
				for ( var x = 0; x < Side; x++ )
				{
					var from = brush.Pixmap.GetPixel(
						x * brush.Pixmap.Width / Side,
						y * brush.Pixmap.Height / Side );

					//Their alpha where they have one, their brightness where they do not - both spellings of a stamp are out there.
					brush.weights[( y * Side ) + x] = from.a < 1.0f ? from.a : from.r;
				}
			}
		}

		return brush;
	}
}
