using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

/// <summary>Compile log dock: warnings from the last compile; clicking one jumps to its node</summary>
public class CompileLog : PanelDockHost
{
	private readonly CompileLogPanel _panel;

	//No parent: the dock manager is what puts a dock somewhere, and a parent handed over here is replaced the moment the dock is opened.
	public CompileLog( Window window ) : base( null )
	{
		Name = "CompileLog";
		WindowTitle = "Compile Log";
		SetWindowIcon( "rule" );

		_panel = new CompileLogPanel( window );

		SetContent( _panel );
	}

	//Read the window's compile again - it has finished, or started over.
	internal void Refresh() => _panel?.Read();
}

//The well the log sits in, and the log.
[StyleSheet( "CompileLog.cs.scss" )]
internal sealed class CompileLogPanel : EditorPanel
{
	private readonly CompileLogList _list;

	public CompileLogPanel( Window window )
	{
		AddClass( "compile-log" );

		_list = AddChild( new CompileLogList( window ) );
		_list.AddClass( "well" );

		Read();
	}

	public void Read() => _list?.Read();
}

//One line per warning, under one line saying what the compile did. Pooled like every other list in the editor even though a clean graph
//has two lines in it: a graph that will not compile has as many as it has mistakes, and that is exactly when somebody is reading this.
internal sealed class CompileLogList : PooledRowList<CompileLogRow>
{
	private readonly Window _window;

	//What the list is over: the summary line, then the warnings.
	private readonly List<Compiler.Warning> _warnings = new();

	private string _summary = string.Empty;
	private CompileState _state;

	//What the summary line is saying, which is what its icon and colour mean.
	internal enum CompileState { Waiting, Compiling, Clean }

	public CompileLogList( Window window )
	{
		_window = window;

		AddClass( "compile-log-list" );
	}

	protected override float RowHeight => Theme.ListRowHeight;

	//The summary is index nought and is always there; the warnings follow it.
	protected override int CellCount => _warnings.Count + 1;

	protected override CompileLogRow CreateCell() => new();

	protected override void BindCell( CompileLogRow cell, int index )
	{
		if ( index == 0 )
		{
			cell.SetSummary( _summary, _state );
			return;
		}

		cell.SetWarning( _warnings[index - 1], Open );
	}

	/// <summary>Reads the compile off the window and puts the list over it.</summary>
	public void Read()
	{
		var warnings = _window.CompileWarnings;

		_warnings.Clear();

		//Errors first: a graph with both is a graph where the errors are what stopped it, and the warnings are
		//what it would have said anyway. While a compile is running there is nothing to say about the last one.
		if ( !_window.IsPreviewCompiling )
			_warnings.AddRange( warnings.OrderByDescending( warning => warning.IsError ) );

		var errors = _warnings.Count( warning => warning.IsError );
		var rest = _warnings.Count - errors;

		_state = _window.IsPreviewCompiling ? CompileState.Compiling
			: _window is { CompileTime: not null } && warnings.Count == 0 ? CompileState.Clean
			: CompileState.Waiting;

		_summary = _window.IsPreviewCompiling
			? "Compiling preview..."
			: _window.CompileTime is { } ms
			? $"Compiled in {ms:0.0} ms" + (errors, rest) switch
			{
				(0, 0) => ", no warnings",
				(0, 1) => ", 1 warning",
				(0, var w) => $", {w} warnings",
				(1, 0) => ", 1 error",
				(var e, 0) => $", {e} errors",
				(var e, var w) => $", {e} error{( e == 1 ? "" : "s" )}, {w} warning{( w == 1 ? "" : "s" )}",
			}
			: "Not compiled yet";

		//The lines are different lines now, not the same ones with new numbers.
		Invalidate();
	}

	private void Open( Compiler.Warning warning )
	{
		if ( warning.Node is not { } node ) return;

		_window.NavigateToNode( node.Identifier );
	}
}

//One line of the compile log: an icon, what it says, and whose fault it is.
internal sealed class CompileLogRow : PanelPooledRow
{
	private readonly UILabel _icon;
	private readonly UILabel _message;
	private readonly UILabel _source;

	private Compiler.Warning _warning;
	private Action<Compiler.Warning> _open;

	public CompileLogRow()
	{
		AddClass( "compile-log-row" );

		//material-icon and grow-shrink are design.scss's: what the icon font is, and which child of a row
		//gives way when the room runs out, are the same answers everywhere and are not this dock's to restate.
		_icon = AddLabel( "icon material-icon" );
		_message = AddLabel( "message grow-shrink" );
		_source = AddLabel( "source" );
	}

	//A label of this row, told not to read its text as tokens.
	private UILabel AddLabel( string classes )
	{
		var label = AddChild<UILabel>( classes );

		label.Tokenize = false;

		return label;
	}

	/// <summary>The line at the top, which is the compile itself rather than a warning.</summary>
	public void SetSummary( string text, CompileLogList.CompileState state )
	{
		_warning = null;
		_open = null;

		SetClass( "summary", true );
		SetClass( "error", false );
		SetClass( "warning", false );
		SetClass( "clickable", false );

		SetClass( "compiling", state == CompileLogList.CompileState.Compiling );
		SetClass( "clean", state == CompileLogList.CompileState.Clean );

		PanelText.Set( _icon, state == CompileLogList.CompileState.Clean ? "check_circle" : "schedule" );
		PanelText.Set( _message, text );
		PanelText.Set( _source, string.Empty );
	}

	/// <summary>A warning, and where to go when it is clicked.</summary>
	public void SetWarning( Compiler.Warning warning, Action<Compiler.Warning> open )
	{
		_warning = warning;
		_open = open;

		SetClass( "summary", false );
		SetClass( "compiling", false );
		SetClass( "clean", false );

		SetClass( "error", warning.IsError );
		SetClass( "warning", !warning.IsError );
		SetClass( "clickable", warning.Node is not null );

		PanelText.Set( _icon, warning.IsError ? "error" : "warning" );
		PanelText.Set( _message, warning.Message );
		PanelText.Set( _source, warning.Node?.DisplayInfo.Name ?? string.Empty );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( _warning is null || _open is null ) return;

		_open( _warning );
	}

	public override void Write( System.Text.StringBuilder into )
	{
		into.Append( _message.Text );

		if ( !string.IsNullOrEmpty( _source.Text ) ) into.Append( '\t' ).Append( _source.Text );
	}
}
