namespace Rinten.UI;

internal static class TextCaretDraw
{
	public static bool TryGet( Label label, out int index, out Color color )
	{
		index = 0;
		color = Color.White;

		if ( label.Parent is not ITextCaretHost host )
			return false;

		if ( !host.ShouldDrawCaret( label ) )
			return false;

		index = host.GetCaretIndex( label );
		color = host.GetCaretColor( label );
		return true;
	}

	public static Rect GetRect( Label label, int index )
	{
		var caret = label.GetCaretRect( index );
		caret.Left = MathX.FloorToInt( caret.Left );
		caret.Width = 1;

		if ( caret.Height < 1 )
			caret.Height = label.ComputedStyle?.FontSize?.GetPixels( 14 ) ?? 14f;

		return caret;
	}
}
