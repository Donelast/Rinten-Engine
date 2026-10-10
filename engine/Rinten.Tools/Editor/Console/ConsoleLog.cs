using Facepunch.ActionGraphs;
using Microsoft.CodeAnalysis;
using Rinten.ActionGraphs;
using System;

namespace Editor;

//Which of the three the status bar counts a line under.
internal enum ConsoleLevel
{
	/// <summary>Anything said in passing - trace and info.</summary>
	Message,

	/// <summary>Something worth a look.</summary>
	Warning,

	/// <summary>Something wrong.</summary>
	Error,
}

//Static console log that outlives the dock: all lines plus indices of those the filter shows
internal static class ConsoleLog
{
	/// <summary>How many lines are kept. Enough to scroll back through a load and a play.</summary>
	public const int MaxEvents = 10000;

	//How many fall off the top at once when it is full. A batch rather than one at a time: dropping the oldest line for each
	//new one is a ten-thousand element move per line logged, and a loop logging every frame is exactly when it would happen.
	private const int TrimBatch = MaxEvents / 10;

	/// <summary>What the logger is called on a line somebody typed in.</summary>
	public const string CommandLogger = "cmd";

	/// <summary>What every log through the bare <c>Log</c> property is named - see GlobalNamespace.Log.</summary>
	public const string DefaultLogger = "Generic";

	private static readonly List<ConsoleLine> lines = new( MaxEvents );

	//Indices into lines of what the filter lets through.
	private static readonly List<int> shown = new( MaxEvents );

	//What the compiler last said, kept so it can be put back after a clear.
	private static readonly List<Diagnostic> diagnostics = new();

	private static readonly int[] counts = new int[3];
	private static readonly bool[] hidden = new bool[3];

	/// <summary>Counts every time what is listed, or what a listed line says, changed.</summary>
	public static int Version { get; private set; }

	/// <summary>How many lines the filter lets through.</summary>
	public static int Count => shown.Count;

	/// <summary>One of those lines.</summary>
	public static ConsoleLine Line( int index ) => lines[shown[index]];

	/// <summary>Something was logged - what the status bar shows the newest of.</summary>
	public static event Action<LogEvent> Logged;

	/// <summary>Lines fell off the top, and everything counting from the top moved by this much.</summary>
	public static event Action<int> Trimmed;

	/// <summary>What is listed is different lines now, and anything holding a place in it is holding a place in the old one.</summary>
	public static event Action Relisted;

	/// <summary>How many lines of a level there have been since the last clear.</summary>
	public static int CountOf( ConsoleLevel level ) => counts[(int)level];

	/// <summary>Whether a level is being kept off the list.</summary>
	public static bool IsHidden( ConsoleLevel level ) => hidden[(int)level];

	/// <summary>Shows or hides a level. What is hidden is still kept, only not listed.</summary>
	public static void SetHidden( ConsoleLevel level, bool value )
	{
		if ( hidden[(int)level] == value ) return;

		hidden[(int)level] = value;

		Refilter();
	}

	/// <summary>Which of the three a line counts under.</summary>
	public static ConsoleLevel LevelOf( LogLevel level ) => level switch
	{
		LogLevel.Warn => ConsoleLevel.Warning,
		LogLevel.Error => ConsoleLevel.Error,
		_ => ConsoleLevel.Message,
	};

	/// <summary>Takes a line, and says whether it changed what is on screen.</summary>
	public static void Add( LogEvent e )
	{
		ThreadSafe.AssertIsMainThread();

		counts[(int)LevelOf( e.Level )]++;

		//The same thing said twice running is one line with a count on it, which is how a loop logging every frame stays readable. Counted
		//against what arrived rather than against what is listed, so a level being hidden does not turn one repeating line into thousands of them.
		if ( lines.Count > 0 )
		{
			var last = lines[^1];

			if ( last.Repeats( e ) )
			{
				last.Event.Repeats++;
				lines[^1] = last;

				Version++;
				Logged?.Invoke( last.Event );

				return;
			}
		}

		lines.Add( new ConsoleLine( e ) );

		if ( !IsHidden( LevelOf( e.Level ) ) ) shown.Add( lines.Count - 1 );

		Trim();

		Version++;
		Logged?.Invoke( e );
	}

	/// <summary>Says a line of the console's own - a command echoed, an answer to one.</summary>
	public static void Say( string text, LogLevel level = LogLevel.Info )
	{
		Add( new LogEvent
		{
			Level = level,
			Logger = CommandLogger,
			Message = text,
			Time = DateTime.Now,
		} );
	}

	/// <summary>Empties the log. What the compiler said comes straight back, because it still holds.</summary>
	public static void Clear()
	{
		lines.Clear();
		shown.Clear();

		Array.Clear( counts );

		Version++;
		Relisted?.Invoke();

		PopulateDiagnostics();
	}

	//Works out the list again, for a level that was turned on or off.
	private static void Refilter( bool relisted = true )
	{
		shown.Clear();

		for ( var at = 0; at < lines.Count; ++at )
		{
			if ( !IsHidden( LevelOf( lines[at].Event.Level ) ) ) shown.Add( at );
		}

		Version++;

		if ( relisted ) Relisted?.Invoke();
	}

	//Drops the oldest lines when there are too many, and says how many listed ones went.
	private static void Trim()
	{
		if ( lines.Count <= MaxEvents ) return;

		lines.RemoveRange( 0, TrimBatch );

		var was = shown.Count;

		Refilter( relisted: false );

		var gone = was - shown.Count;

		if ( gone > 0 ) Trimmed?.Invoke( gone );
	}

	//Takes the compiler's warnings and errors as lines of their own. They are not logs - nobody wrote them out - but they are what somebody wants
	//the moment a build fails, and the console is where they look. Held separately so a clear does not lose them: they are still true afterwards.
	[Event( "compile.complete" )]
	private static void OnCompileComplete( CompileGroup compileGroup )
	{
		DropDiagnosticLines();

		diagnostics.Clear();
		diagnostics.AddRange( compileGroup.Compilers.Where( x => x.Diagnostics != null ).SelectMany( x => x.Diagnostics ) );

		//The counts are about this build, not about every build there has been.
		Array.Clear( counts );

		PopulateDiagnostics();
	}

	[Event( "actiongraph.saved" )]
	private static void OnActionGraphSaved( ActionGraph actionGraph )
	{
		DropDiagnosticLines();

		Array.Clear( counts );

		PopulateDiagnostics();
	}

	//Forgets the lines that came from a diagnostic, so they can be said again.
	private static void DropDiagnosticLines()
	{
		var removed = lines.RemoveAll( x => x.Event.IsDiagnostic );

		if ( removed == 0 ) return;

		Refilter();
	}

	//Says what the compiler and the action graphs have to say, as lines.
	private static void PopulateDiagnostics()
	{
		//Only warnings and errors: a diagnostics report is otherwise every suggestion the analysers have about every file in the project.
		foreach ( var diagnostic in diagnostics.Where( x => x.Severity >= DiagnosticSeverity.Warning ).OrderBy( x => x.Severity ) )
		{
			Add( Of( diagnostic ) );
		}

		foreach ( var graph in EditorNodeLibrary.GetGraphs() )
		{
			if ( !graph.HasErrors() ) continue;

			foreach ( var message in graph.Messages.Where( x => x.IsError ) )
			{
				Add( Of( message ) );
			}
		}
	}

	private static LogEvent Of( Diagnostic diagnostic )
	{
		var msg = diagnostic.GetMessage();
		var span = diagnostic.Location.GetLineSpan();
		var mappedSpan = diagnostic.Location.GetMappedLineSpan();

		return new LogEvent
		{
			IsDiagnostic = true,

			Message = msg,
			HtmlMessage = $"<a href=\"{diagnostic.Descriptor.HelpLinkUri}\">{diagnostic.Descriptor.Id}</a> {msg}",
			Logger = "Compiler",
			Time = DateTime.Now,
			Level = diagnostic.Severity switch
			{
				DiagnosticSeverity.Error => LogLevel.Error,
				DiagnosticSeverity.Warning => LogLevel.Warn,
				_ => LogLevel.Info
			},

			//A stack of our own, since diagnostics do not carry one (and should not).
			Stack = $"at {msg} in {(mappedSpan.HasMappedPath ? mappedSpan.Path : span.Path)}:line {(mappedSpan.Span.Start.Line + 1)}"
		};
	}

	private static LogEvent Of( ValidationMessage message )
	{
		var msg = message.Value;

		return new LogEvent
		{
			IsDiagnostic = true,

			Message = msg,
			HtmlMessage = msg,
			Logger = "ActionGraph",
			Time = DateTime.Now,
			Level = message.Level switch
			{
				MessageLevel.Error => LogLevel.Error,
				MessageLevel.Warning => LogLevel.Warn,
				_ => LogLevel.Info
			},

			Stack = $"at {message.Context.StackTraceIdentifier}"
		};
	}

	[Event( "scene.startplay" )]
	private static void OnStartPlay()
	{
		if ( EditorPreferences.ClearConsoleOnPlay ) Clear();
	}
}
