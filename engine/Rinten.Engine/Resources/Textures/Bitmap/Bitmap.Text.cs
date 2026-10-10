using Rinten.UI;

namespace Rinten;

public partial class Bitmap
{
	/// <summary>Draws text onto this bitmap</summary>
	public void DrawText( TextRendering.Scope scope, Rect rect, TextFlag flags = TextFlag.Center )
	{
		using var block = new TextRendering.TextBlock( scope, new Vector2( 0, 0 ), flags );
		block.Render( _canvas, rect );
	}

	//Draws a block already made - and measured, so it is not shaped a second time.
	internal void DrawText( TextRendering.TextBlock block, Rect rect )
	{
		block.Render( _canvas, rect );
	}

	//What a widget that draws itself asks before it draws: how wide a label is decides where everything beside it goes.
	/// <summary>How much room this text would take, and where inside the rectangle it would end up, without drawing any of it.</summary>
	public static Rect MeasureText( TextRendering.Scope scope, Rect within, TextFlag flags = TextFlag.Center )
	{
		using var block = new TextRendering.TextBlock( scope, new Vector2( 0, 0 ), flags );

		return block.Measure( within );
	}
}
