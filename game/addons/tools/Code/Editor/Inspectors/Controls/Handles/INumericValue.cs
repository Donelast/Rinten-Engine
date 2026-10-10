namespace Editor.Inspectors;

/// <summary>A control's number read and written as a double, whatever type actually stores it</summary>
public interface INumericValue
{
	/// <summary>False while there is nothing to write to, or writing is not allowed.</summary>
	bool IsEditable { get; }

	/// <summary>The number itself.</summary>
	double Number { get; set; }

	/// <summary>Opens an edit that <see cref="FinishEdit"/> closes, so a drag that writes the number a hundred times is one step to undo rather than a hundred.</summary>
	void BeginEdit();

	/// <summary>Closes the edit <see cref="BeginEdit"/> opened.</summary>
	void FinishEdit();
}
