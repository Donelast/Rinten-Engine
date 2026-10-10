using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Rinten;

//Editor console panel: shows the engine log and takes a command line
internal sealed class EditorConsole : Panel
{
	/// <summary>How many lines are kept. Older ones fall off the top.</summary>
	public const int Lines = 400;

	private readonly Panel log;
	private readonly EditorTextEntry entry;
	private readonly Queue<Panel> lines = new();

	//Log events queued and turned into panels next tick, since logging can happen mid-layout
	private readonly System.Collections.Concurrent.ConcurrentQueue<LogEvent> waiting = new();

	private bool listening;

	public EditorConsole()
	{
		AddClass( "console" );

		log = AddChild<Panel>();
		log.AddClass( "log" );

		//Follows the newest line unless somebody has scrolled up to read.
		log.PreferScrollToBottom = true;

		var bar = AddChild<Panel>();
		bar.AddClass( "entry" );

		entry = bar.AddChild<EditorTextEntry>();
		entry.Placeholder = "enter a command";
		entry.Submitted = Run;

		var run = bar.AddChild<EditorButton>();
		run.Text = "Run";
		run.AddClass( "primary" );
		run.Pressed = () => Run( entry.Text );
	}

	public override void OnParentChanged()
	{
		base.OnParentChanged();

		Listen( Parent is not null );
	}

	public override void OnDeleted()
	{
		Listen( false );

		base.OnDeleted();
	}

	//Whether we are on the end of the engine's log. Taken and let go with the panel: a
	//console that is gone still holding the log keeps every panel it ever made alive with it.
	private void Listen( bool wanted )
	{
		if ( listening == wanted ) return;

		listening = wanted;

		if ( !wanted )
		{
			Diagnostics.Logging.OnMessage -= Logged;
			return;
		}

		//What was said before there was anywhere to say it: a console opened after the engine
		//started is otherwise empty, and everything worth reading happened during the start.
		foreach ( var already in Diagnostics.Logging.Recent )
			waiting.Enqueue( already );

		Diagnostics.Logging.OnMessage += Logged;
	}

	//Runs what was typed and says so, the way a console does - a command with no output is otherwise indistinguishable from one that never ran.
	private void Run( string command )
	{
		if ( string.IsNullOrWhiteSpace( command ) ) return;

		entry.Clear();

		Write( "> " + command, "command", null );

		try
		{
			ConsoleSystem.Run( command );
		}
		catch ( Exception e )
		{
			Write( e.Message, "error", null );
		}
	}

	//One line of the engine's log, as it happens.
	private void Logged( LogEvent entry ) => waiting.Enqueue( entry );

	/// <summary>Everything said since the last frame, shown. Once a frame, before the layout rather than during somebody else's.</summary>
	public override void Tick()
	{
		base.Tick();

		var shown = 0;

		//A frame that logs thousands of lines - a stack trace loop - must not take the window with it; the rest wait for the next one.
		while ( shown++ < Lines && waiting.TryDequeue( out var entry ) )
			Show( entry );
	}

	private void Show( LogEvent entry )
	{
		var kind = entry.Level switch
		{
			LogLevel.Error => "error",
			LogLevel.Warn => "warn",
			LogLevel.Trace => "trace",
			_ => "info",
		};

		Write( entry.Message, kind, entry.Logger );
	}

	private void Write( string message, string kind, string from )
	{
		if ( message is null ) return;

		var line = log.AddChild<Panel>();
		line.AddClass( "line" );
		line.AddClass( kind );

		if ( !string.IsNullOrEmpty( from ) )
		{
			var source = line.AddChild<Label>();
			source.AddClass( "from" );
			source.Text = from;
			source.Tokenize = false;
		}

		var text = line.AddChild<Label>();
		text.AddClass( "what" );
		text.Text = message;

		//The log is not markup and a message that happens to contain a bracket is not a tag;
		//nor is a message beginning with a hash a phrase to look up in the language files.
		text.Tokenize = false;

		lines.Enqueue( line );

		while ( lines.Count > Lines && lines.TryDequeue( out var oldest ) )
			oldest.Delete( true );

		//The newest line is the one being read.
		log.TryScrollToBottom();
	}
}
