using System;
using System.Diagnostics;
using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;

namespace Editor.Mcp;

/// <summary>The project's code compilers and the console, as tools. These live in the engine rather than in the tools addon on purpose: when that addon's own compile fails, every tool in it is gone with it - and a compile failure is exactly when an agent needs to ask what went wrong.</summary>
[McpToolset( "editor" )]
internal static class CompileTools
{
	//How long compile_wait gives the file watcher to notice an edit before concluding that nothing was queued. A save
	//is usually seen within a moment, and the watcher has been seen to take three seconds over one - this is past that.
	static readonly TimeSpan WatcherGrace = TimeSpan.FromSeconds( 5 );

	[McpTool( "console_command" )]
	[Description( "Run a console command - anything the editor console accepts, including setting convars. Output, if any, lands in the console - read_console shows it." )]
	public static object ConsoleCommand( [Description( "The command line to run, e.g. 'stat fps' or 'sv_cheats 1'." )] string command )
	{
		if ( string.IsNullOrWhiteSpace( command ) )
			throw new Exception( "Give a command to run" );

		var since = LogBuffer.Last;

		ConsoleSystem.Run( command );

		return LogBuffer.TextSince( since ) ?? "Ran. Nothing was logged - if the command prints later, read_console shows it.";
	}

	[McpTool.ReadOnly( "compile_status" ), McpListed]
	[Description( "The live state of the project's code compilers - the game, its addons and the editor's own tool projects: is anything building, did each compiler's last build succeed, and every error and warning with file and line. This is how you check whether a code edit hotloaded cleanly. To wait for a build you just caused instead of polling, call compile_wait." )]
	public static CompileState CompileStatus()
	{
		return Read( Group() );
	}

	[McpTool.ReadOnly( "compile_wait" ), McpListed]
	[Description( "Wait for the build a code edit queued to finish, then report it like compile_status. Call it right after writing .cs or .razor files: the file watcher marks the compiler for rebuild within a moment, this waits for that, then for the build itself, and returns the outcome with every error and warning. Returns immediately with Queued=false when no build starts within a few seconds - the file is probably outside any project's code folder. Times out rather than blocks forever; call again if it does." )]
	public static async Task<CompileWaitResult> CompileWait(
		[Description( "How long to wait for the build to finish, in seconds. Default 120, max 600." ), Range( 1, 600 )] int timeoutSeconds = 120 )
	{
		var group = Group();
		var since = LogBuffer.Last;
		var started = Stopwatch.StartNew();
		var timeout = TimeSpan.FromSeconds( timeoutSeconds );

		//Something already queued or building counts as the build we're waiting for.
		var queued = Pending( group );

		while ( !queued && started.Elapsed < WatcherGrace )
		{
			await Frame.Next();
			queued = Pending( group );
		}

		if ( queued )
		{
			await Frame.Until( () => !Pending( group ), timeout,
				() => $"Still building after {timeoutSeconds}s. Call compile_wait again to keep waiting, or compile_status to see the state so far." );
		}

		return new CompileWaitResult
		{
			Queued = queued,
			WaitedSeconds = (float)Math.Round( started.Elapsed.TotalSeconds, 1 ),
			Note = queued ? null : $"No build was queued within {WatcherGrace.TotalSeconds:0} seconds. Watched code folders are each project's Code and Editor directories - a file elsewhere doesn't compile. This is the current state, not the result of a build.",
			State = Read( group ),
			Console = LogBuffer.TextSince( since )
		};
	}

	static CompileGroup Group()
	{
		return Project.CompileGroup ?? throw new Exception( "No compile group - is a project loaded? editor_status says." );
	}

	static bool Pending( CompileGroup group ) => group.NeedsBuild || group.IsBuilding;

	static CompileState Read( CompileGroup group )
	{
		return new CompileState
		{
			IsBuilding = group.IsBuilding,
			NeedsBuild = group.NeedsBuild,
			Compilers = group.Compilers.Select( x =>
			{
				var diagnostics = x.Diagnostics;

				return new CompilerState
				{
					Name = x.Name,
					IsBuilding = x.IsBuilding,
					NeedsBuild = x.NeedsBuild,
					Success = x.Output?.Successful,
					Errors = diagnostics.Count( d => d.Severity == DiagnosticSeverity.Error ),
					Warnings = diagnostics.Count( d => d.Severity == DiagnosticSeverity.Warning ),
					Diagnostics = diagnostics
						.Where( d => d.Severity >= DiagnosticSeverity.Warning )
						.OrderByDescending( d => d.Severity )
						.Take( 50 )
						.Select( d =>
						{
							//Mapped: the compiler builds generated files - a .razor's class, a .cs with its
							//attributes added - and the #line directives in them point back at what the user wrote.
							var span = d.Location.GetMappedLineSpan();

							return new CompileDiagnostic
							{
								Severity = d.Severity.ToString(),
								Id = d.Id,
								Message = d.GetMessage(),
								File = span.Path,
								Line = span.StartLinePosition.Line + 1
							};
						} ).ToArray()
				};
			} ).ToArray()
		};
	}
}

//The live state of every compiler in the project's compile group.
internal class CompileState
{
	[Description( "Whether anything is building right now." )]
	public bool IsBuilding { get; set; }

	[Description( "Whether a build is queued but not yet started - an edit the watcher saw, waiting for the next frame." )]
	public bool NeedsBuild { get; set; }

	[Description( "Every compiler, each with its last build's outcome." )]
	public CompilerState[] Compilers { get; set; }
}

//One compiler and how its last build went.
internal class CompilerState
{
	[Description( "The project this compiler builds, like 'base', 'toolbase' or the game's ident." )]
	public string Name { get; set; }

	public bool IsBuilding { get; set; }

	public bool NeedsBuild { get; set; }

	[Description( "Whether the last build succeeded. Null when it hasn't built yet." )]
	public bool? Success { get; set; }

	public int Errors { get; set; }

	public int Warnings { get; set; }

	[Description( "The last build's errors and warnings, errors first, capped at 50." )]
	public CompileDiagnostic[] Diagnostics { get; set; }
}

//One compiler error or warning and where it happened.
internal class CompileDiagnostic
{
	public string Severity { get; set; }

	[Description( "The diagnostic id, like 'CS0246'." )]
	public string Id { get; set; }

	public string Message { get; set; }

	public string File { get; set; }

	public int Line { get; set; }
}

//What compile_wait reports.
internal class CompileWaitResult
{
	[Description( "Whether a build was seen to be queued or running. False means nothing built - State is just the current state." )]
	public bool Queued { get; set; }

	public float WaitedSeconds { get; set; }

	[Description( "Why nothing was waited for, when Queued is false." )]
	public string Note { get; set; }

	public CompileState State { get; set; }

	[Description( "What the console logged while waiting - compile errors are printed there too, with their full text." )]
	public string Console { get; set; }
}
