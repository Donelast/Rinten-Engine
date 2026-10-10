using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>A message with an answer or two, over the window that asked.</summary>
public sealed class PopupWindow
{
	private readonly string title;
	private readonly string text;
	private readonly string dismiss;
	private readonly List<EditorModal.Choice> answers = new();

	public PopupWindow( string title, string text, string buttonTxt = "OK", IDictionary<string, Action> extraButtons = null )
	{
		this.title = title;
		this.text = text;

		//A dialog with nothing but a way out is a notice: its one button is the answer, and there is nothing to cancel.
		this.dismiss = extraButtons is { Count: > 0 } ? buttonTxt : null;

		if ( extraButtons is { Count: > 0 } )
		{
			foreach ( var (name, action) in extraButtons )
				answers.Add( new EditorModal.Choice( name, action, Removes( name ) ) );
		}
		else
		{
			answers.Add( new EditorModal.Choice( buttonTxt, null ) );
		}
	}

	//Whether an answer takes something away, which is the red button. Read off the word on it. Every one of these that removes something
	//says so - "Delete", "Discard", "Remove" - and a dialog that colours its answer by what it does is the whole reason the colour is there.
	private static bool Removes( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return false;

		foreach ( var word in new[] { "delete", "remove", "discard", "clear", "reset" } )
		{
			if ( name.Contains( word, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	/// <summary>Puts it up, over whichever window is being used.</summary>
	public void Show() => EditorModal.Choose( null, title, text, dismiss, answers );
}
