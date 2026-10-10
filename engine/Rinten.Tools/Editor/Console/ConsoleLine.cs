using System;
using System.Text.RegularExpressions;

namespace Editor;

//A console line with its display data parsed once on arrival, not on every scroll
internal struct ConsoleLine
{
	//The first stack frame with a file on it, matched against the same shape StackTraceRows.DefaultStackLineHandler
	//reads, and against what a diagnostic's own synthesised stack already is.
	private static readonly Regex SourceLineRegex = new( @"^at .+? in (.+):line (\d+)$", RegexOptions.Compiled );

	/// <summary>The event itself. Not readonly, because a line said twice counts up.</summary>
	public LogEvent Event;

	/// <summary>The message as the one line there is room for.</summary>
	public readonly string Text;

	/// <summary>The file the line came from, where any frame named one.</summary>
	public readonly string SourceFile;

	/// <summary>Which line of that file.</summary>
	public readonly int SourceLine;

	/// <summary>What the source says on the row, or nothing where there is none.</summary>
	public readonly string SourceLabel;

	/// <summary>Which sort of line this is, as the class the sheet colours it by.</summary>
	public readonly string Tone;

	public ConsoleLine( LogEvent e )
	{
		Event = e;

		Text = OneLine( e.Message );
		Tone = ToneOf( e );

		(SourceFile, SourceLine, SourceLabel) = ParseSource( e.Stack );
	}

	/// <summary>Whether this line and an event are the same thing said again.</summary>
	public readonly bool Repeats( in LogEvent e ) =>
		Event.Message == e.Message && Event.Logger == e.Logger && Event.Stack == e.Stack;

	//What a line's words are coloured by: what sort of line it is.
	private static string ToneOf( in LogEvent e )
	{
		if ( e.Logger == ConsoleLog.CommandLogger ) return "command";

		return e.Level switch
		{
			LogLevel.Warn => "warn",
			LogLevel.Error => "error",
			LogLevel.Trace => "trace",
			_ => "info",
		};
	}

	//A message as one line, whatever it was written as. A message with returns in it is shown as the one line it is given room
	//for, and the rest is a double-click away. The whole string is kept on the event, so nothing is lost by this - only not shown.
	private static string OneLine( string message )
	{
		if ( string.IsNullOrEmpty( message ) ) return string.Empty;

		var breaks = message.IndexOfAny( ['\r', '\n', '\t'] );

		if ( breaks < 0 ) return message;

		var single = message.Replace( "\t", "    " );

		breaks = single.IndexOfAny( ['\r', '\n'] );

		return breaks < 0 ? single : single[..breaks] + " ...";
	}

	//A line's source from its stack, skipping Logger.cs frames by file rather than by count
	private static (string File, int Line, string Label) ParseSource( string stack )
	{
		if ( string.IsNullOrEmpty( stack ) ) return (null, 0, null);

		foreach ( var raw in stack.Split( '\n', '\r' ) )
		{
			if ( string.IsNullOrWhiteSpace( raw ) ) continue;

			var match = SourceLineRegex.Match( raw.Trim() );
			if ( !match.Success ) continue;

			var file = match.Groups[1].Value;

			if ( file.Contains( @"Rinten.System\Logging\", StringComparison.OrdinalIgnoreCase ) ||
				file.Contains( "Rinten.System/Logging/", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var line = match.Groups[2].Value.ToInt();

			//The number first. A console is read down a column, and what tells two lines of the same file apart is
			//the number - so it is what the eye should land on, not what it has to read past a filename to find.
			return (file, line, $"{line}  {System.IO.Path.GetFileName( file )}");
		}

		return (null, 0, null);
	}
}
