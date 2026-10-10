namespace Rinten;

/// <summary>Represents a sprite resource that can be static or animated. Sprites are rendererd using the SpriteRenderer component.</summary>
[AssetType( Name = "Sprite", Extension = "sprite", Category = "Rendering", Icon = "emoji_emotions" )]
public sealed partial class Sprite : GameResource
{
	/// <summary>A list of animations that can be played. Some animations can consist of multiple frames. If a sprite is static, it will only contain a single default animation.</summary>
	[Property, InlineEditor, WideMode]
	public List<Animation> Animations { get; set; } = new()
	{
		//Default animation
		new()
		{
			Name = "Default",
			Frames = [new Frame { Texture = Texture.White }]
		}
	};

	/// <summary>Get the index of an animation by its name. Returns -1 if not found.</summary>
	public int GetAnimationIndex( string name )
	{
		for ( int i = 0; i < Animations.Count; i++ )
		{
			if ( string.Equals( Animations[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
			{
				return i;
			}
		}
		return -1; //Not found
	}

	/// <summary>Get an animation by its index. Returns null if out of bounds.</summary>
	public Animation GetAnimation( int index )
	{
		if ( index < 0 || index >= Animations.Count )
		{
			return null;
		}
		return Animations[index];
	}

	/// <summary>Get an animation by its name. Returns null if not found.</summary>
	public Animation GetAnimation( string name )
	{
		int index = GetAnimationIndex( name );
		if ( index == -1 )
		{
			return null; //Not found
		}
		return GetAnimation( index );
	}

	/// <summary>Returns a sprite with a single frame animation using the provided texture.</summary>
	public static Sprite FromTexture( Texture texture )
	{
		return new Sprite()
		{
			Animations = [
					new() {
						Name = "Default",
						Frames = [
							new() { Texture = texture }
						]
					}
				]
		};
	}

	/// <summary>Returns a sprite with a single animation using the provided textures as frames.</summary>
	public static Sprite FromTextures( IEnumerable<Texture> textures, float frameRate = 15f )
	{
		var frames = textures.Select( t => new Frame { Texture = t } );
		return new Sprite()
		{
			Animations = [
					new() {
						Name = "Default",
						FrameRate = frameRate,
						Frames = frames.ToList()
					}
				]
		};
	}

	/// <summary>Returns a sprite with a single animation cut out of one sheet: a grid of equal cells read left to right, top to bottom, with <paramref name="spacing"/> pixels between them.</summary>
	public static Sprite FromSheet( Texture sheet, Vector2Int cellSize, IEnumerable<int> cells, float frameRate = 15f, int spacing = 0 )
	{
		return new Sprite()
		{
			Animations = [
					new() {
						Name = "Default",
						FrameRate = frameRate,
						Frames = cells.Select( c => new Frame { Texture = sheet, Region = SheetCell( sheet, cellSize, c, spacing ) } ).ToList()
					}
				]
		};
	}

	/// <summary>The pixels of one cell of a sheet laid out as a grid of <paramref name="cellSize"/>, counted from nought at the top left, left to right and then down.</summary>
	public static RectInt SheetCell( Texture sheet, Vector2Int cellSize, int cell, int spacing = 0 )
	{
		var across = Math.Max( 1, ( ( sheet?.Width ?? cellSize.x ) + spacing ) / Math.Max( 1, cellSize.x + spacing ) );
		return new RectInt( cell % across * ( cellSize.x + spacing ), cell / across * ( cellSize.y + spacing ), cellSize.x, cellSize.y );
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		var svg = "<svg viewBox=\"0 0 128 128\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" aria-hidden=\"true\" role=\"img\" class=\"iconify iconify--noto\" preserveAspectRatio=\"xMidYMid meet\" fill=\"#000000\"><g id=\"SVGRepo_bgCarrier\" stroke-width=\"0\"></g><g id=\"SVGRepo_tracerCarrier\" stroke-linecap=\"round\" stroke-linejoin=\"round\"></g><g id=\"SVGRepo_iconCarrier\"><path fill=\"#8fe637\" d=\"M30.47 104.24h13.39v13.39H30.47z\"></path><path fill=\"#8fe637\" d=\"M84.04 104.24h13.39v13.39H84.04z\"></path><path fill=\"#8fe637\" d=\"M30.48 10.51h13.39V23.9H30.48z\"></path><path fill=\"#8fe637\" d=\"M84.04 10.51h13.39V23.9H84.04z\"></path><radialGradient id=\"IconifyId17ecdb2904d178eab5528\" cx=\"64.344\" cy=\"9.403\" r=\"83.056\" gradientUnits=\"userSpaceOnUse\"><stop offset=\".508\" stop-color=\"#8fe637\"></stop><stop offset=\".684\" stop-color=\"#8fe637\"></stop><stop offset=\".878\" stop-color=\"#8fe637\"></stop><stop offset=\".981\" stop-color=\"#8fe637\"></stop></radialGradient><path d=\"M97.46 64.08V37.3H84.04V23.9H70.65v13.4H57.26V23.9H43.87v13.4H30.48v26.78H17.09v13.39h13.39v13.4h13.39v13.38h13.39V90.87h13.39v13.38h13.39V90.87h13.42v-13.4h13.37V64.08H97.46zm-40.21 0H43.86V50.69h13.39v13.39zm26.78 0H70.64V50.69h13.39v13.39z\" fill=\"url(#IconifyId17ecdb2904d178eab5528)\"></path><radialGradient id=\"IconifyId17ecdb2904d178eab5529\" cx=\"63.118\" cy=\"24.114\" r=\"65.281\" gradientUnits=\"userSpaceOnUse\"><stop offset=\".508\" stop-color=\"#8fe637\"></stop><stop offset=\".684\" stop-color=\"#8fe637\"></stop><stop offset=\".878\" stop-color=\"#8fe637\"></stop><stop offset=\".981\" stop-color=\"#8fe637\"></stop></radialGradient><path fill=\"url(#IconifyId17ecdb2904d178eab5529)\" d=\"M110.82 37.29h13.4v26.8h-13.4z\"></path><radialGradient id=\"IconifyId17ecdb2904d178eab5530\" cx=\"62.811\" cy=\"13.081\" r=\"75.09\" gradientUnits=\"userSpaceOnUse\"><stop offset=\".508\" stop-color=\"#8fe637\"></stop><stop offset=\".684\" stop-color=\"#8fe637\"></stop><stop offset=\".878\" stop-color=\"#8fe637\"></stop><stop offset=\".981\" stop-color=\"#8fe637\"></stop></radialGradient><path fill=\"url(#IconifyId17ecdb2904d178eab5530)\" d=\"M3.7 37.28h13.4v26.8H3.7z\"></path></g></svg>";
		return Bitmap.CreateFromSvgString( svg, width, height );
	}
}

