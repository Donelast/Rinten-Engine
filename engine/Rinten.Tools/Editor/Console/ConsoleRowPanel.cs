using Rinten.UI;
using System;
using System.Globalization;

namespace Editor;

//One line of the console: when it happened, where it came from, how many times running, and what was said. Four labels and no paint method.
//What it costs to exist is what a row of the profiler costs - see PanelPooledRow - and only the ones the window can reach exist at all.
internal sealed class ConsoleRowPanel : PanelPooledRow
{
	//How much room a formatted time needs, with some to spare.
	private const int TimeRoom = 16;

	//The column the source sits in - what the pointer is tested against.
	private readonly Panel sourceCell;

	private readonly Rinten.UI.Label time;
	private readonly Rinten.UI.Label source;
	private readonly Rinten.UI.Label repeats;
	private readonly Rinten.UI.Label message;

	private string tone;

	/// <summary>Pressed on, which is where picking lines out starts.</summary>
	public Action<ConsoleRowPanel> Pressed { get; set; }

	/// <summary>The pointer arrived, which is how a pick under way grows.</summary>
	public Action<ConsoleRowPanel> Entered { get; set; }

	public ConsoleRowPanel()
	{
		//Not one of the banded lists. A console is a wall of text being read down, and striping it is movement nobody asked for while reading.
		//What a line says about itself - under the pointer, last looked at, picked out - the console's own sheet says, in its own three steps.
		SetClass( "list-row", false );

		AddClass( "console-row" );

		//The two fixed columns are a panel with a label in it rather than the label itself: a label is measured by its own text, and a width
		//on one is a suggestion. The same reason PanelListRow's columns are built this way, and the reason the messages line up down the page.
		time = Cell( "time" );

		source = Cell( "source" );
		sourceCell = source.Parent;

		repeats = AddChild<Rinten.UI.Label>( "repeats" );
		repeats.Tokenize = false;

		message = AddChild<Rinten.UI.Label>( "message" );
		message.Tokenize = false;
	}

	//A fixed-width column with one label in it.
	private Rinten.UI.Label Cell( string classname )
	{
		var label = AddChild<Panel>( classname ).AddChild<Rinten.UI.Label>( "text" );

		label.Tokenize = false;

		return label;
	}

	/// <summary>Whether the pointer is on the part of the row that names a file.</summary>
	public bool IsOverSource => hasSource && sourceCell.HasHovered;

	private bool hasSource;

	/// <summary>Puts this row over a line.</summary>
	public void Bind( in ConsoleLine line )
	{
		SetTone( line.Tone );

		Span<char> buffer = stackalloc char[TimeRoom];

		if ( line.Event.Time.TryFormat( buffer, out var written, "HH:mm:ss", CultureInfo.InvariantCulture ) )
			PanelText.Set( time, buffer[..written] );
		else
			PanelText.Set( time, string.Empty );

		hasSource = line.SourceLabel is not null;

		PanelText.Set( source, line.SourceLabel ?? string.Empty );
		sourceCell.SetClass( "link", hasSource );

		if ( line.Event.Repeats > 0 )
		{
			PanelText.Count( repeats, line.Event.Repeats, "n0", "[", "]" );
			repeats.SetClass( "hidden", false );
		}
		else
		{
			PanelText.Set( repeats, string.Empty );
			repeats.SetClass( "hidden", true );
		}

		PanelText.Set( message, line.Text );
	}

	/// <summary>The two things that are a state of the line rather than of the pointer.</summary>
	public void SetMarks( bool picked, bool selected )
	{
		SetClass( "picked", picked );
		SetClass( "current", selected );
	}

	/// <summary>Drops what this row was holding while it waits out of the window.</summary>
	public void Unbind()
	{
		Index = -1;
		hasSource = false;

		PanelText.Set( message, string.Empty );
		PanelText.Set( source, string.Empty );
		PanelText.Set( repeats, string.Empty );
	}

	//Which sort of line it is. A class, so an unchanged row does not re-cascade.
	private void SetTone( string value )
	{
		if ( tone == value ) return;

		if ( tone is not null ) SetClass( tone, false );

		tone = value;

		SetClass( value, true );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Pressed?.Invoke( this );
	}

	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );

		Entered?.Invoke( this );
	}

	//Deliberately nothing: the right button opens the log's own menu, which is the list's
	//to open, and the base would put this one line on the clipboard on the way past.
	protected override void OnRightClick( MousePanelEvent e )
	{
	}
}
