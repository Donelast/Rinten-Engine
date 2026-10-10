using Rinten;
using Rinten.UI;
using System;

namespace Editor;

//What Paint draws on.
internal sealed class PaintCanvas
{
	public Bitmap Bitmap { get; }

	public PaintCanvas( Bitmap bitmap )
	{
		Bitmap = bitmap;
	}

	/// <summary>Whether anything at all was drawn on it.</summary>
	public bool Drew { get; private set; }


	public Color BrushColour { get; private set; } = Color.White;
	public bool HasBrush { get; private set; } = true;

	public Color PenColour { get; private set; } = Color.White;
	public float PenSize { get; private set; } = 1.0f;
	public PenStyle PenStyle { get; private set; } = PenStyle.Solid;
	public bool HasPen { get; private set; }

	private Gradient? brushGradient;
	private Vector2 gradientFrom;
	private Vector2 gradientTo;
	private float gradientRadius;
	private bool gradientRadial;

	public string FontName { get; private set; } = "Inter";
	public float FontSize { get; private set; } = 12.0f;
	public int FontWeight { get; private set; } = 400;
	public bool FontItalic { get; private set; }

	public void SetBrush( Color colour )
	{
		BrushColour = colour;
		HasBrush = true;
		brushGradient = null;
	}

	public void ClearBrush()
	{
		HasBrush = false;
		brushGradient = null;
	}

	public void SetPen( Color colour, float size, PenStyle style )
	{
		PenColour = colour;
		PenSize = size <= 0.0f ? 1.0f : size;
		PenStyle = style;
		HasPen = style != PenStyle.None;
	}

	public void SetPenColour( Color colour )
	{
		PenColour = colour;
		HasPen = PenStyle != PenStyle.None;
	}

	public void SetPenSize( float size )
	{
		PenSize = size <= 0.0f ? 1.0f : size;
	}

	public void SetPenStyle( PenStyle style )
	{
		PenStyle = style;
		HasPen = style != PenStyle.None;
	}

	public void ClearPen()
	{
		HasPen = false;
	}

	public void SetBrushLinear( Vector2 from, Vector2 to, Color a, Color b )
	{
		brushGradient = new Gradient( new Gradient.ColorFrame( 0.0f, a ), new Gradient.ColorFrame( 1.0f, b ) );

		gradientFrom = from;
		gradientTo = to;
		gradientRadial = false;
		HasBrush = true;
	}

	public void SetBrushRadial( Vector2 centre, float radius, float at, Color a, float to, Color b )
	{
		brushGradient = new Gradient( new Gradient.ColorFrame( at, a ), new Gradient.ColorFrame( to, b ) );

		gradientFrom = centre;
		gradientRadius = radius;
		gradientRadial = true;
		HasBrush = true;
	}

	public void SetFont( string name, float size, int weight, bool italic )
	{
		if ( !string.IsNullOrEmpty( name ) ) FontName = name;

		FontSize = size;
		FontWeight = weight;
		FontItalic = italic;
	}


	public void Rect( Rect rect, float radius = 0.0f )
	{
		if ( radius > 0.0f )
		{
			Rect( rect, radius, radius, radius, radius );
			return;
		}

		Fill( () => Bitmap.DrawRect( rect ) );
		Stroke( () => Bitmap.DrawRect( rect ) );
	}

	/// <summary>Rect with per-corner radii; maps named corners to the Margin order the bitmap reads</summary>
	public void Rect( Rect rect, float topLeft, float topRight, float bottomRight, float bottomLeft )
	{
		var corners = new Margin( topLeft, topRight, bottomRight, bottomLeft );

		Fill( () => Bitmap.DrawRoundRect( rect, corners ) );
		Stroke( () => Bitmap.DrawRoundRect( rect, corners ) );
	}

	public void Circle( Rect rect )
	{
		var centre = rect.Center;
		var radius = MathF.Min( rect.Width, rect.Height ) * 0.5f;

		Fill( () => Bitmap.DrawCircle( centre, radius ) );
		Stroke( () => Bitmap.DrawCircle( centre, radius ) );
	}

	public void Arc( Rect rect, float start, float sweep, bool pie )
	{
		if ( pie ) Fill( () => Bitmap.DrawArc( rect, start, sweep, true ) );

		Stroke( () => Bitmap.DrawArc( rect, start, sweep, pie ) );
	}

	public void Polygon( Vector2[] points )
	{
		if ( points is null || points.Length < 2 ) return;

		Fill( () => Bitmap.DrawPolygon( points ) );
		Stroke( () => Bitmap.DrawPolygon( points ) );
	}

	public void Line( Vector2 from, Vector2 to )
	{
		Stroke( () => Bitmap.DrawLine( from, to ), always: true );
	}

	public void Lines( Vector2[] points )
	{
		if ( points is null || points.Length < 2 ) return;

		Stroke( () => Bitmap.DrawLines( points ), always: true );
	}

	/// <summary>A picture, drawn into a rectangle.</summary>
	public void Image( Rect rect, Pixmap pixmap, float alpha = 1.0f )
	{
		if ( pixmap?.picture is null || !pixmap.picture.IsValid ) return;

		Bitmap.SetFill( Color.White.WithAlpha( alpha ) );
		Bitmap.DrawBitmap( pixmap.picture, rect );

		Drew = true;
	}

	public void ImageTiled( Rect rect, Pixmap pixmap, Vector2 origin, Vector2 scale, float alpha = 1.0f )
	{
		if ( pixmap?.picture is null || !pixmap.picture.IsValid ) return;

		Bitmap.SetFill( Color.White.WithAlpha( alpha ) );
		Bitmap.FillTiled( pixmap.picture, rect, origin, scale );

		Drew = true;
	}

	public Rect Text( Rect rect, string text, TextFlag flags )
	{
		if ( string.IsNullOrEmpty( text ) ) return new Rect( rect.Position, 0 );

		var scope = Scope( text );
		var where = Bitmap.MeasureText( scope, rect, flags );

		Bitmap.DrawText( scope, rect, flags );

		Drew = true;

		return where;
	}

	/// <summary>Where the text would go, without drawing it.</summary>
	public Rect MeasureText( Rect rect, string text, TextFlag flags )
	{
		if ( string.IsNullOrEmpty( text ) ) return new Rect( rect.Position, 0 );

		return Bitmap.MeasureText( Scope( text ), rect, flags );
	}

	private TextRendering.Scope Scope( string text )
	{
		return new TextRendering.Scope( text, HasPen ? PenColour : BrushColour, FontSize, FontName, FontWeight )
		{
			FontItalic = FontItalic,
		};
	}

	//Filling and outlining, which is where the state above meets the picture.

	private void Fill( System.Action draw )
	{
		if ( !HasBrush ) return;

		if ( brushGradient is { } gradient )
		{
			if ( gradientRadial ) Bitmap.SetRadialGradient( gradientFrom, gradientRadius, gradient );
			else Bitmap.SetLinearGradient( gradientFrom, gradientTo, gradient );
		}
		else
		{
			Bitmap.SetFill( BrushColour );
		}

		draw();
		Drew = true;
	}

	private void Stroke( System.Action draw, bool always = false )
	{
		if ( !HasPen && !always ) return;

		var colour = HasPen ? PenColour : BrushColour;

		if ( PenStyle is PenStyle.Dash or PenStyle.Dot or PenStyle.DashDot or PenStyle.DashDotDot )
		{
			Bitmap.SetDashedPen( colour, PenSize, Dashes( PenStyle, PenSize ) );
		}
		else
		{
			Bitmap.SetPen( colour, PenSize );
		}

		draw();
		Drew = true;
	}

	private static float[] Dashes( PenStyle style, float width ) => style switch
	{
		PenStyle.Dash => new[] { 4 * width, 2 * width },
		PenStyle.Dot => new[] { 1 * width, 2 * width },
		PenStyle.DashDot => new[] { 4 * width, 2 * width, 1 * width, 2 * width },
		PenStyle.DashDotDot => new[] { 4 * width, 2 * width, 1 * width, 2 * width, 1 * width, 2 * width },
		_ => null,
	};
}
