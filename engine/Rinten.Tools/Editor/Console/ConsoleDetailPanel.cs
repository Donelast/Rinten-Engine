using Rinten.UI;
using System;

namespace Editor;

//One log line in full with its stack, shown beside the log rather than in a modal
internal sealed class ConsoleDetailPanel : Panel
{
	private readonly Rinten.UI.Label icon;
	private readonly Rinten.UI.Label logger;
	private readonly Rinten.UI.Label time;
	private readonly Rinten.UI.Label message;
	private readonly Panel frames;

	private string tone;

	/// <summary>Asked to be put away.</summary>
	public Action Closed { get; set; }

	public ConsoleDetailPanel()
	{
		AddClass( "console-detail" );

		var head = AddChild<Panel>( "head" );

		icon = head.AddChild<Rinten.UI.Label>( "icon" );
		icon.Tokenize = false;

		logger = head.AddChild<Rinten.UI.Label>( "logger" );
		logger.Tokenize = false;

		time = head.AddChild<Rinten.UI.Label>( "time" );
		time.Tokenize = false;

		head.AddChild<Panel>( "stretch" );

		var copy = head.AddChild<ConsoleDetailButton>();
		copy.SetIcon( "content_copy" );
		copy.Tooltip = "Copy the message and its stack";
		copy.Pressed = Copy;

		var close = head.AddChild<ConsoleDetailButton>();
		close.SetIcon( "close" );
		close.Tooltip = "Close this half";
		close.Pressed = () => Closed?.Invoke();

		var body = AddChild<Panel>( "body" );

		message = body.AddChild<Rinten.UI.Label>( "message" );
		message.Tokenize = false;

		frames = body.AddChild<Panel>( "frames" );
	}

	//What is being shown, so the same line clicked again is not built twice.
	private LogEvent shown;

	/// <summary>Shows a line.</summary>
	public void Show( in ConsoleLine line )
	{
		var e = line.Event;

		if ( shown.Message == e.Message && shown.Stack == e.Stack && shown.Time == e.Time ) return;

		shown = e;

		SetTone( line.Tone );

		PanelText.Set( icon, e.Level switch
		{
			LogLevel.Error => "error",
			LogLevel.Warn => "warning",
			_ => "info"
		} );

		PanelText.Set( logger, string.IsNullOrEmpty( e.Logger ) ? "Log" : e.Logger );
		PanelText.Set( time, e.Time.ToString( "HH:mm:ss" ) );
		PanelText.Set( message, e.Message ?? string.Empty );

		frames.DeleteChildren( immediate: true );

		foreach ( var row in StackTraceRows.Read( e.Stack ) )
		{
			frames.AddChild( new ConsoleFramePanel( row ) );
		}
	}

	//Puts the message and the whole stack on the clipboard.
	private void Copy()
	{
		var text = shown.Message ?? string.Empty;

		if ( !string.IsNullOrEmpty( shown.Stack ) ) text += $"\n{shown.Stack}";

		EditorUtility.Clipboard.Copy( text );
	}

	private void SetTone( string value )
	{
		if ( tone == value ) return;

		if ( tone is not null ) SetClass( tone, false );

		tone = value;

		SetClass( value, true );
	}
}

//One frame of the stack, as a row you can press.
internal sealed class ConsoleFramePanel : Panel
{
	private readonly StackRow row;

	public ConsoleFramePanel( StackRow row )
	{
		this.row = row;

		AddClass( "console-frame" );

		SetClass( "engine", row.IsFromEngine );
		SetClass( "action", row.IsFromAction );
		SetClass( "placeless", row.FileName is null );
		SetClass( "pressable", row.MouseClick is not null );

		var name = AddChild<Rinten.UI.Label>( "function" );
		name.Tokenize = false;
		name.Text = row.FunctionName ?? string.Empty;

		if ( row.FileName is null ) return;

		var file = AddChild<Rinten.UI.Label>( "file" );
		file.Tokenize = false;
		file.Text = row.FileName;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		row.MouseClick?.Invoke();
	}
}

//A small icon button on the detail's own bar.
internal sealed class ConsoleDetailButton : Panel
{
	private readonly Rinten.UI.Label icon;

	/// <summary>Run when it is pressed.</summary>
	public Action Pressed { get; set; }

	public ConsoleDetailButton()
	{
		AddClass( "console-detail-button" );

		icon = AddChild<Rinten.UI.Label>( "icon" );
		icon.Tokenize = false;
	}

	public void SetIcon( string name ) => PanelText.Set( icon, name );

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Pressed?.Invoke();
	}
}
