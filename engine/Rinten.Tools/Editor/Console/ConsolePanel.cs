using Rinten.UI;
using System;

namespace Editor;

//Console with a resizable split: log, plus details of the clicked line once one is clicked
internal sealed class ConsolePanel : EditorPanel
{
	//Where the border between the two halves is remembered.
	private const string SplitCookie = "Console.Split";

	//Which cell of the splitter the detail is.
	private const int DetailCell = 1;

	private readonly SplitterPanel split;
	private readonly ConsoleListPanel list;
	private readonly ConsoleDetailPanel detail;
	private readonly ConsoleInputPanel input;
	private readonly ConsoleSuggestions suggestions;

	public ConsolePanel()
	{
		AddClass( "console" );

		EditorStyles.Apply( this, "ConsolePanel.cs.scss" );

		var body = AddChild<Panel>( "body" );

		split = body.AddChild( new SplitterPanel( vertical: false ) );

		list = new ConsoleListPanel { Chosen = Show };
		split.AddCell( list, 6.0f );

		detail = new ConsoleDetailPanel { Closed = Hide };
		split.AddCell( detail, 4.0f );

		split.RestoreState( EditorCookie.Get( SplitCookie, string.Empty ) );
		split.SetCellShown( DetailCell, false );

		//Over the log rather than under the field, because the field is already at the bottom of the dock and there is nothing below it to open into.
		suggestions = body.AddChild<ConsoleSuggestions>();
		suggestions.Picked = command => input.Take( command );

		var bar = AddChild<PanelToolbar>();
		bar.AddClass( "console-bar" );

		input = bar.AddChild<ConsoleInputPanel>();
		input.Placeholder = "Type help for every command, or start typing for suggestions";
		input.Suggestions = suggestions;
		input.Submitted = Run;

		//There used to be a second field here to filter what the log showed, which nobody used: the log is read by scrolling it and narrowed by the
		//three level buttons on the status bar, and a box that quietly hid lines is a console that has lost messages as far as anybody looking can tell.
		bar.AddIcon( "delete", "Clear the console log", ConsoleLog.Clear );
		bar.AddIcon( "vertical_align_bottom", "Scroll to bottom of console log", list.ScrollToBottom );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		//Where somebody dragged the border to is theirs, not this session's.
		EditorCookie.Set( SplitCookie, split.SaveState() );
	}

	//Shows a line whole, in the half beside the log.
	private void Show( int index )
	{
		if ( index < 0 || index >= ConsoleLog.Count )
		{
			Hide();
			return;
		}

		detail.Show( ConsoleLog.Line( index ) );

		split.SetCellShown( DetailCell, true );
	}

	//Puts that half away, and gives the log the whole dock again.
	private void Hide() => split.SetCellShown( DetailCell, false );

	/// <summary>Puts the caret in the field, for whoever pressed the console key.</summary>
	public void FocusInput() => input.Focus();

	/// <summary>Whether the field can be typed into at all - see <see cref="FullScreenManager"/>.</summary>
	public void SetInputEnabled( bool enabled )
	{
		input.AcceptsFocus = enabled;

		if ( !enabled ) input.Blur();
	}

	//Runs what was typed, and says it into the log on the way.
	private void Run( string text )
	{
		var command = text.Trim();

		if ( command.Length == 0 ) return;

		input.Reset();
		input.Remember( command );

		//For them typists
		if ( command == "clear" )
		{
			ConsoleLog.Clear();
			return;
		}

		//The command, echoed as a line of its own.
		ConsoleLog.Say( $"> {command}" );

		//Ours rather than the console system's: it is about this window - what there is to type into it - and it prints into this window's log.
		if ( IsHelp( command ) ) PrintHelp( command );
		else ConsoleSystem.Run( command );

		list.ScrollToBottom();
	}

	//Whether a line is asking for the list of commands. help is the one, and the two others are what a person types when they
	//are coming from a shell or a chat box. A console that answers "unknown command" to any of them has told you nothing twice.
	private static bool IsHelp( string command )
	{
		var word = command.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault() ?? "";

		return word is "help" or "/help" or "?";
	}

	//Prints commands and descriptions into the log; a word after help filters them
	private static void PrintHelp( string command )
	{
		var parts = command.Split( ' ', 2, StringSplitOptions.RemoveEmptyEntries );
		var filter = parts.Length > 1 ? parts[1].Trim() : null;

		var commands = ConsoleSystem.GetAll()
			.Where( x => filter is null
				|| x.Name.Contains( filter, StringComparison.OrdinalIgnoreCase )
				|| (x.Help ?? "").Contains( filter, StringComparison.OrdinalIgnoreCase ) )
			.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.ToList();

		if ( commands.Count == 0 )
		{
			ConsoleLog.Say( filter is null ? "No commands." : $"No command matches '{filter}'." );
			return;
		}

		//Names in one column so the descriptions line up under each other - the list is read by running down the left edge until something looks right.
		var width = Math.Min( commands.Max( x => x.Name.Length ), 34 );

		ConsoleLog.Say( filter is null
			? $"{commands.Count} command(s). Type help <word> to narrow it down."
			: $"{commands.Count} command(s) matching '{filter}'." );

		foreach ( var entry in commands )
		{
			var name = entry.Name.PadRight( width );
			var help = string.IsNullOrWhiteSpace( entry.Help ) ? "" : $"  {entry.Help}";

			ConsoleLog.Say( $"{name}{help}" );
		}
	}
}
