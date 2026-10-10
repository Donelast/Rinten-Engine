namespace Rinten.UI;

/// <summary>Lets a <see cref="Label"/> draw a caret after its text texture.</summary>
public interface ITextCaretHost
{
	bool ShouldDrawCaret( Label label );
	int GetCaretIndex( Label label );
	Color GetCaretColor( Label label );
}
