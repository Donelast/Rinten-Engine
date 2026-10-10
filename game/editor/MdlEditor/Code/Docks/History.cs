using Rinten.Helpers;
using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

/// <summary>Undo history dock: one row per change; clicking one takes the graph back to that point</summary>
public class History : PanelDockHost
{
	private readonly HistoryPanel _panel;

	//No parent: the dock manager is what puts a dock somewhere, and a parent handed over here is replaced the moment the dock is opened.
	public History( Window window ) : base( null )
	{
		Name = "History";
		WindowTitle = "History";
		SetWindowIcon( "history" );

		_panel = new HistoryPanel( window );

		SetContent( _panel );
	}
}

//The undo and redo buttons, and the history under them. An EditorPanel rather than a Panel, so it
//hears the editor's own events itself - the dock above it does not have to forward a frame down.
[StyleSheet( "History.cs.scss" )]
internal sealed class HistoryPanel : EditorPanel
{
	private readonly Window _window;
	private readonly UndoSystem _undo;
	private readonly HistoryList _list;

	private readonly PanelToolbarButton _back;
	private readonly PanelToolbarButton _forward;
	private readonly PanelToolbarButton _clear;

	private int _seen = -1;

	public HistoryPanel( Window window )
	{
		_window = window;
		_undo = window.UndoSystem;

		AddClass( "history" );

		var bar = AddChild<PanelToolbar>();

		_back = bar.AddIcon( "undo", "Undo", () => { _window.Undo(); Read(); } );
		_forward = bar.AddIcon( "redo", "Redo", () => { _window.Redo(); Read(); } );
		_clear = bar.AddIcon( "playlist_remove", "Clear History", () => { _undo.Initialize(); Read(); } );

		bar.AddStretch();

		_list = AddChild( new HistoryList( this ) );
		_list.AddClass( "well" );

		Read();
	}

	//A frame rather than an event: the history moves for anything the editor does at all, and two integer comparisons cost less than wiring every one of them up. Nothing while the dock is not on screen.
	/// <summary>Reads the undo system again, where it has moved since the last look.</summary>
	[EditorEvent.Frame]
	public void OnFrame()
	{
		if ( !IsVisible ) return;

		Poll();
	}

	/// <summary>Which entry the graph is standing on. Everything past it has been undone.</summary>
	public int Level { get; private set; }

	/// <summary>The history in reading order: done oldest first, then undone; rebuilt once per change</summary>
	public List<UndoSystem.Entry> Entries { get; } = new();

	/// <summary>Reads the history again where its shape has changed since the last frame.</summary>
	public void Poll()
	{
		var shape = HashCode.Combine( _undo.Back.Count, _undo.Forward.Count );

		if ( _seen == shape ) return;

		Read();
	}

	/// <summary>Reads the history, whatever it did.</summary>
	public void Read()
	{
		Level = _undo.Back.Count + 1;
		_seen = HashCode.Combine( _undo.Back.Count, _undo.Forward.Count );

		Entries.Clear();
		Entries.AddRange( _undo.Back.Reverse() );
		Entries.AddRange( _undo.Forward );

		var canUndo = _undo.Back.Count > 0;
		var canRedo = _undo.Forward.Count > 0;

		_back.Enabled = canUndo;
		_forward.Enabled = canRedo;
		_clear.Enabled = canUndo || canRedo;

		_back.Tooltip = _undo.Back.TryPeek( out var undone ) ? $"Undo {undone.Name}" : "Undo";
		_forward.Tooltip = _undo.Forward.TryPeek( out var redone ) ? $"Redo {redone.Name}" : "Redo";

		_list.Invalidate();
	}

	/// <summary>Winds the graph to the row that was clicked, whichever side of now it is.</summary>
	public void JumpTo( int row )
	{
		var target = row + 1;
		var at = _undo.Back.Count + 1;

		while ( at > target && _window.Undo() ) at--;
		while ( at < target && _window.Redo() ) at++;

		Read();
	}
}

//The history as a list: the state it opened in, then everything done to it, then everything that has been undone and could be done again.
internal sealed class HistoryList : PooledRowList<HistoryRow>
{
	private readonly HistoryPanel _panel;

	public HistoryList( HistoryPanel panel )
	{
		_panel = panel;

		AddClass( "history-list" );
	}

	protected override float RowHeight => Theme.ListRowHeight;

	//The opening state, and then one row per entry either side of now.
	protected override int CellCount => _panel.Entries.Count + 1;

	protected override HistoryRow CreateCell() => new();

	protected override void BindCell( HistoryRow cell, int index )
	{
		cell.Index = index;
		cell.Clicked = row => _panel.JumpTo( row.Index );

		var level = _panel.Level;
		var undone = index >= level;
		var here = index == level - 1;

		if ( index == 0 )
		{
			cell.Set( "Opened", string.Empty, undone, here );
			return;
		}

		var entry = _panel.Entries[index - 1];

		cell.Set( entry.Name, entry.Timestamp.ToLocalTime().ToString( "HH:mm:ss" ), undone, here );
	}
}

//One change: whether the graph is standing on it, what it was, and when.
internal sealed class HistoryRow : PanelPooledRow
{
	private readonly UILabel _marker;
	private readonly UILabel _name;
	private readonly UILabel _time;

	public HistoryRow()
	{
		AddClass( "history-row" );

		_marker = AddLabel( "marker material-icon" );
		_name = AddLabel( "name grow-shrink" );
		_time = AddLabel( "time" );
	}

	//A label of this row, told not to read its text as tokens.
	private UILabel AddLabel( string classes )
	{
		var label = AddChild<UILabel>( classes );

		label.Tokenize = false;

		return label;
	}

	/// <summary>What this row says, and where it stands against now.</summary>
	public void Set( string name, string time, bool undone, bool here )
	{
		SetClass( "undone", undone );
		SetClass( "here", here );

		PanelText.Set( _marker, here ? "arrow_right" : string.Empty );
		PanelText.Set( _name, name );
		PanelText.Set( _time, time );
	}

	public override void Write( System.Text.StringBuilder into )
	{
		into.Append( _name.Text );

		if ( !string.IsNullOrEmpty( _time.Text ) ) into.Append( '\t' ).Append( _time.Text );
	}
}
