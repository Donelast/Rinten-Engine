namespace Editor.Utility;

/// <summary>Creates placeholder thumbnail icons.</summary>
public static class PlaceholderIcon
{
	static (Color start, Color end) GetGradientColors()
	{
		var i = Random.Shared.Next( 10 );

		return i switch
		{
			1 => new( "#374785", "#a8d0e6" ),
			2 => new( "#fe5f55", "#f4d35e" ),
			3 => new( "#00a8e8", "#007ea7" ),
			4 => new( "#f76c6c", "#ffb3b3" ),
			5 => new( "#2a9d8f", "#e9c46a" ),
			6 => new( "#8d99ae", "#edf2f4" ),
			7 => new( "#ef476f", "#ffd166" ),
			8 => new( "#06d6a0", "#118ab2" ),
			9 => new( "#b56576", "#e56b6f" ),
			_ => new( "#ffbe0b", "#fb5607" ),
		};
	}

	private static string GetInitials( string name )
	{
		var normalizedName = name.Replace( '_', ' ' ).Replace( '-', ' ' );
		var words = normalizedName.Split( ' ', StringSplitOptions.RemoveEmptyEntries );

		if ( words.Length == 0 )
			return string.Empty;

		var initials = string.Join( "", words.Take( 3 ).Select( w => char.ToUpper( w.First() ) ) );

		return initials;
	}

	//How much of the icon the initials are allowed to take.
	private const float Fill = 0.8f;

	//The smallest they are allowed to be shrunk to.
	private const float Smallest = 10.0f;

	/// <summary>Generates a placeholder icon with initials at the requested size.</summary>
	public static Pixmap Generate( string name, int size, string fontFamily = null )
	{
		if ( string.IsNullOrWhiteSpace( fontFamily ) ) fontFamily = Theme.DefaultFont;

		var initials = GetInitials( name );
		var (start, end) = GetGradientColors();

		using var bitmap = new Bitmap( size, size, false );

		bitmap.SetLinearGradient( 0, size, Gradient.FromColors( start, end ) );
		bitmap.DrawRect( bitmap.Rect );

		var outline = new TextRendering.Outline() { Enabled = true, Color = Color.Black.WithAlpha( 0.2f ), Size = 8 };

		TextRendering.Scope Text( float at ) =>
			new TextRendering.Scope( initials, Color.White, at, fontFamily, 900 ) { Outline = outline };

		const TextFlag flags = TextFlag.Center | TextFlag.DontClip;

		//Shrink font until three letters fit, measured with the same text path used to draw
		var fontSize = size * 0.5f;

		while ( fontSize > Smallest )
		{
			var measured = Bitmap.MeasureText( Text( fontSize ), bitmap.Rect, flags );

			if ( measured.Width <= size * Fill && measured.Height <= size * Fill ) break;

			fontSize -= 1.0f;
		}

		bitmap.DrawText( Text( fontSize ), bitmap.Rect, flags );

		return Pixmap.FromBitmap( bitmap );
	}
}
