using Rinten.UI;
using System;

namespace Editor;

//Completions shown above the input (it sits at the bottom); tab walks them, arrows stay history
internal sealed class ConsoleSuggestions : Panel
{
	//How many are shown. More than this is a list nobody reads.
	private const int MaxOptions = 8;

	private readonly ConsoleSuggestionRow[] rows = new ConsoleSuggestionRow[MaxOptions];
	private readonly string[] commands = new string[MaxOptions];

	private int count;
	private int at = -1;

	/// <summary>One was chosen with the pointer.</summary>
	public Action<string> Picked { get; set; }

	public ConsoleSuggestions()
	{
		AddClass( "console-suggestions" );

		SetClass( "hidden", true );
	}

	/// <summary>Whether there is anything to take.</summary>
	public bool IsOpen => count > 0;

	/// <summary>Works out what could be meant, and shows it - or nothing at all.</summary>
	public void Show( string partial )
	{
		count = 0;

		if ( !string.IsNullOrWhiteSpace( partial ) )
		{
			foreach ( var option in EditorUtility.AutoComplete( partial, MaxOptions ) )
			{
				if ( count >= MaxOptions ) break;

				Row( count ).Bind( option.Command, option.Description );
				commands[count] = option.Command;

				count++;
			}
		}

		//One suggestion that is already what was typed has nothing to add.
		if ( count == 1 && string.Equals( commands[0], partial, StringComparison.OrdinalIgnoreCase ) ) count = 0;

		for ( var index = 0; index < rows.Length; ++index )
		{
			rows[index]?.SetClass( "hidden", index >= count );
		}

		at = count > 0 ? 0 : -1;

		SetClass( "hidden", count == 0 );

		Mark();
	}

	/// <summary>Puts the list away.</summary>
	public void Close()
	{
		if ( count == 0 ) return;

		count = 0;
		at = -1;

		SetClass( "hidden", true );
	}

	/// <summary>The one the highlight is on, and moves the highlight to the next - so tab after tab walks the list with the field showing where it has got to.</summary>
	public string Step( bool back )
	{
		if ( count == 0 ) return null;

		if ( at < 0 ) at = 0;

		var taken = commands[at];

		at = back
			? (at - 1 + count) % count
			: (at + 1) % count;

		Mark();

		return taken;
	}

	private void Mark()
	{
		for ( var index = 0; index < count; ++index )
		{
			rows[index].SetClass( "current", index == at );
		}
	}

	//The row at a place in the list, made the first time it is wanted. Kept and rewritten rather than built again: this is asked
	//for on every letter typed, and a list rebuilt out of new panels each time is eight panels deleted and eight made per keystroke.
	private ConsoleSuggestionRow Row( int index )
	{
		if ( rows[index] is { } made ) return made;

		var row = AddChild<ConsoleSuggestionRow>();

		row.Clicked = () => Picked?.Invoke( commands[index] );

		rows[index] = row;

		return row;
	}
}

//One thing that could be meant: what to type, and what it does.
internal sealed class ConsoleSuggestionRow : Panel
{
	private readonly Rinten.UI.Label command;
	private readonly Rinten.UI.Label help;

	/// <summary>Chosen with the pointer.</summary>
	public Action Clicked { get; set; }

	public ConsoleSuggestionRow()
	{
		AddClass( "console-suggestion" );

		command = AddChild<Rinten.UI.Label>( "command" );
		command.Tokenize = false;

		help = AddChild<Rinten.UI.Label>( "help" );
		help.Tokenize = false;
	}

	public void Bind( string name, string description )
	{
		PanelText.Set( command, name ?? string.Empty );
		PanelText.Set( help, description ?? string.Empty );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Clicked?.Invoke();
	}
}
