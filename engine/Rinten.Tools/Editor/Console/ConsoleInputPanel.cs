using Rinten.UI;
using System;

namespace Editor;

//Command input: up/down walk history in the field, tab walks suggestions
internal sealed class ConsoleInputPanel : TextField
{
	//Where what was typed is kept between sessions.
	private const string Cookie = "Console.History";

	//How many lines back it remembers.
	private const int MaxHistory = 50;

	private List<string> history;

	//How far back through the history the field is standing, or minus one.
	private int at = -1;

	//What was in the field before walking back into the history.
	private string typed;

	/// <summary>The list of what could be meant, where there is one to work with.</summary>
	public ConsoleSuggestions Suggestions { get; set; }

	public ConsoleInputPanel()
	{
		//A control's own sheet is written against its element name, and an element name is the type's own - so
		//a TextField subclass would arrive with none of TextField's styling on it. This is one, and says so.
		ElementName = "textfield";

		AddClass( "console-input" );

		history = EditorCookie.Get( Cookie, new List<string>() ) ?? new List<string>();
	}

	/// <summary>Takes a line that was run, so up brings it back.</summary>
	public void Remember( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return;

		history.RemoveAll( x => x == text );
		history.Add( text );

		while ( history.Count > MaxHistory ) history.RemoveAt( 0 );

		//Back to the near end of it. Running a line is the end of walking through the old ones, and the next up should be what was just run.
		at = -1;
		typed = null;

		EditorCookie.Set( Cookie, history );
	}

	/// <summary>Empties the field, and stands it at the near end of the history again.</summary>
	public void Reset()
	{
		at = -1;
		typed = null;

		Replace( string.Empty );

		Suggestions?.Close();
	}

	/// <summary>Puts a command in the field, as if it had been typed there.</summary>
	public void Take( string command )
	{
		Replace( command );

		Suggestions?.Close();

		Focus();
	}

	public override void OnKeyTyped( char k )
	{
		base.OnKeyTyped( k );

		Suggest();
	}

	public override void OnPaste( string text )
	{
		base.OnPaste( text );

		Suggest();
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( !e.Pressed )
		{
			base.OnButtonTyped( e );
			return;
		}

		switch ( e.Button )
		{
			case "tab":
				//Swallowed either way: tab in a console finishes a word, and the one thing it must not do is hand the focus to the next control.
				e.StopPropagation = true;

				if ( Suggestions?.Step( e.HasShift ) is { } completion ) Replace( completion );

				return;

			case "up":
				e.StopPropagation = true;

				Cycle( back: true );
				return;

			case "down":
				e.StopPropagation = true;

				Cycle( back: false );
				return;

			case "escape":
				//The list first: escape with suggestions up means put them away, not empty what has been typed.
				if ( Suggestions is { IsOpen: true } )
				{
					e.StopPropagation = true;

					Suggestions.Close();
					return;
				}

				break;

			case "enter":
			case "return":
				Suggestions?.Close();
				break;

			case "backspace":
			case "delete":
				base.OnButtonTyped( e );

				Suggest();
				return;
		}

		base.OnButtonTyped( e );
	}

	protected override void OnBlur( PanelEvent e )
	{
		base.OnBlur( e );

		Suggestions?.Close();
	}

	private void Suggest() => Suggestions?.Show( Value );

	//Steps one entry back or forward through the history, into the field.
	private void Cycle( bool back )
	{
		if ( history.Count == 0 ) return;

		Suggestions?.Close();

		if ( back )
		{
			//The first step remembers whatever was half-typed, so coming back down returns it rather than an empty field.
			if ( at < 0 )
			{
				typed = Value;
				at = history.Count - 1;
			}
			else if ( at > 0 )
			{
				at--;
			}
			else
			{
				//Already at the oldest. Stay there rather than wrapping round to the newest, which loses your place.
				return;
			}
		}
		else
		{
			if ( at < 0 ) return;

			at++;

			if ( at >= history.Count )
			{
				at = -1;

				Replace( typed ?? string.Empty );
				typed = null;

				return;
			}
		}

		Replace( history[at] );
	}
}
