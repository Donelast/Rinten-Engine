namespace Rinten;

//Shared bits of the engine's own asset types.
internal static class EssResource
{
	/// <summary>An asset icon that is only the icon - no tile behind it.</summary>
	public static Bitmap DrawGlyph( string icon, int width, int height, Color color )
	{
		var bitmap = new Bitmap( width, height );

		//Fully transparent, so nothing is painted where the glyph is not.
		bitmap.Clear( new Color( 0, 0, 0, 0 ) );

		//A shade under the full height: a glyph drawn edge to edge looks cramped next to ones that are not.
		var scope = new TextRendering.Scope( icon, color, height * 0.72f, "Material Icons" );
		bitmap.DrawText( scope, bitmap.Rect, TextFlag.Center | TextFlag.DontClip );

		return bitmap;
	}
}
