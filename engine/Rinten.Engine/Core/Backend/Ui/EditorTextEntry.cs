using Rinten.UI;
using System;

namespace Rinten;

//Engine-side text entry for editor windows open before game content loads; the label does editing
internal sealed class EditorTextEntry : Panel
{
	public override bool AcceptsImeInput => true;

	private readonly Label label;
	private string placeholder;

	/// <summary>What was typed, as it is typed.</summary>
	public Action<string> Edited { get; set; }

	/// <summary>What was typed, when return is pressed.</summary>
	public Action<string> Submitted { get; set; }

	public EditorTextEntry()
	{
		AcceptsFocus = true;
		AddClass( "text-entry" );

		label = AddChild<Label>();
		label.Tokenize = false;
		label.Multiline = false;
		label.Style.WhiteSpace = WhiteSpace.Pre;
	}

	/// <summary>The text in it.</summary>
	public string Text
	{
		get => label.Text ?? string.Empty;
		set
		{
			if ( label.Text == value ) return;

			label.Text = value;
			label.SetCaretPosition( label.TextLength );

			ShowPlaceholder();
		}
	}

	/// <summary>What it says while it is empty and nobody is typing into it.</summary>
	public string Placeholder
	{
		get => placeholder;
		set
		{
			placeholder = value;
			ShowPlaceholder();
		}
	}

	/// <summary>Empties it, as submitting a command line does.</summary>
	public void Clear()
	{
		label.Text = string.Empty;
		label.SetCaretPosition( 0 );

		ShowPlaceholder();
	}

	private void ShowPlaceholder()
	{
		SetClass( "placeholder", string.IsNullOrEmpty( label.Text ) && !string.IsNullOrEmpty( placeholder ) );

		if ( HasClass( "placeholder" ) && !HasFocus )
		{
			//Shown by the label rather than by a second one on top of it: two labels means two
			//sets of text to keep in step, and this one is replaced the moment anything is typed.
			label.Text = placeholder;
		}
	}

	protected override void OnClick( MousePanelEvent e )
	{
		Focus();
	}

	/// <summary>Handles return, backspace and arrows on press only; releases are swallowed but not acted on</summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		//For the release as well: a field being typed into is deaf to everything above it for the whole of a keystroke, not half of one.
		e.StopPropagation = true;

		if ( !e.Pressed ) return;

		switch ( e.Button )
		{
			case "enter":
			case "keypadenter":
				Submitted?.Invoke( Text );
				return;

			case "backspace":
				if ( label.HasSelection() ) label.ReplaceSelection( string.Empty );
				else if ( label.CaretPosition > 0 ) label.RemoveText( label.CaretPosition - 1, 1 );

				Changed();
				return;

			case "delete":
				if ( label.HasSelection() ) label.ReplaceSelection( string.Empty );
				else if ( label.CaretPosition < label.TextLength ) label.RemoveText( label.CaretPosition, 1 );

				Changed();
				return;

			case "left":
				label.MoveCaretPos( -1, e.HasShift );
				return;

			case "right":
				label.MoveCaretPos( 1, e.HasShift );
				return;

			case "home":
				label.MoveToLineStart( e.HasShift );
				return;

			case "end":
				label.MoveToLineEnd( e.HasShift );
				return;

			case "a" when e.HasCtrl:
				label.SetSelection( 0, label.TextLength );
				return;
		}
	}

	/// <summary>A letter, as the platform composed it.</summary>
	public override void OnKeyTyped( char key )
	{
		if ( label.HasSelection() ) label.ReplaceSelection( string.Empty );

		label.InsertText( key.ToString(), label.CaretPosition );
		label.MoveCaretPos( 1 );

		Changed();
	}

	protected override void OnFocus( PanelEvent e )
	{
		//The placeholder is not text anybody typed, so it goes the moment the field is somebody's to type into.
		if ( HasClass( "placeholder" ) )
		{
			label.Text = string.Empty;
			SetClass( "placeholder", false );
		}
	}

	protected override void OnBlur( PanelEvent e )
	{
		ShowPlaceholder();
	}

	private void Changed()
	{
		SetClass( "placeholder", false );

		Edited?.Invoke( Text );
	}
}
