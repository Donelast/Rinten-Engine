using System;

namespace Editor;

//Measuring text without a picture to draw it on.
internal static class EditorText
{
	/// <summary>How big the editor's own text is, in pixels.</summary>
	public const float Size = 12.0f;

	/// <summary>The same size in points, which is what <c>Paint</c>'s font calls take unless they are told the number is pixels.</summary>
	public const float SizeInPoints = Size * 72.0f / 96.0f;

	//Somewhere for text to be measured in, of no particular size.
	private static readonly Rect Anywhere = new( 0, 0, 4096, 4096 );

	//Text measure cache keyed by text, size, font and weight; layout re-measures labels many times
	private static readonly Dictionary<(string Text, float Size, string Font, int Weight), Vector2> measured = new();

	//How many measurements are kept before the lot is thrown away.
	private const int Remembered = 8192;

	public static Vector2 Measure( string text, float size = Size, string font = null, int weight = 400 )
	{
		if ( string.IsNullOrEmpty( text ) ) return Vector2.Zero;

		var key = (text, size, font ?? Theme.DefaultFont, weight);

		lock ( measured )
		{
			if ( measured.TryGetValue( key, out var remembered ) ) return remembered;
		}

		var scope = new TextRendering.Scope( text, Color.White, size, key.Item3, weight );

		var found = Bitmap.MeasureText( scope, Anywhere, TextFlag.LeftTop ).Size;

		lock ( measured )
		{
			if ( measured.Count >= Remembered ) measured.Clear();

			measured[key] = found;
		}

		return found;
	}

	/// <summary>Size of text wrapped to a given width; Measure assumes one line</summary>
	public static Vector2 MeasureWrapped( string text, float width, float size = Size, string font = null, int weight = 400 )
	{
		if ( string.IsNullOrEmpty( text ) ) return Vector2.Zero;
		if ( width <= 1.0f ) return Measure( text, size, font, weight );

		//To the pixel: a width that drifts by a hundredth between passes is a different entry in here every frame, and the same words measured again.
		width = MathF.Ceiling( width );

		var key = (text, width, size, font ?? Theme.DefaultFont, weight);

		lock ( wrapped )
		{
			if ( wrapped.TryGetValue( key, out var remembered ) ) return remembered;
		}

		var scope = new TextRendering.Scope( text, Color.White, size, key.Item4, weight );

		var found = Bitmap.MeasureText( scope, new Rect( 0, 0, width, 4096 ), TextFlag.LeftTop | TextFlag.WordWrap ).Size;

		lock ( wrapped )
		{
			if ( wrapped.Count >= Remembered ) wrapped.Clear();

			wrapped[key] = found;
		}

		return found;
	}

	//The same, for text measured inside a column of a stated width.
	private static readonly Dictionary<(string Text, float Width, float Size, string Font, int Weight), Vector2> wrapped = new();

	/// <summary>Forgets what was measured, for when the letters themselves change.</summary>
	[EditorEvent.Hotload]
	public static void Forget()
	{
		lock ( measured ) measured.Clear();
		lock ( wrapped ) wrapped.Clear();
	}
}
