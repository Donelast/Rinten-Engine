using Diagnostic = Microsoft.CodeAnalysis.Diagnostic;
using DiagnosticSeverity = Microsoft.CodeAnalysis.DiagnosticSeverity;
using Rinten.Engine;
using System;

namespace Editor;

//Startup failures shown as a ProblemSheet over the window, since the console is hidden under the blur
static partial class StartupLoadProject
{
	//What the card up is about - which says what a finished build does to it.
	enum ProblemKind
	{
		None,

		/// <summary>The editor's own projects did not build: a fix is picked up by a restart.</summary>
		BuiltIns,

		/// <summary>The project's code did not build: it recompiles in place, and the card goes once it has.</summary>
		ProjectCode,

		/// <summary>Nothing a build changes - an exception, a failed open.</summary>
		Other,
	}

	//The card that is up, if one is. One at a time: a second problem joins the first.
	static ProblemSheet problems;

	static ProblemKind problemKind;

	//The project this editor was asked to open - what a restart opens again.
	static string startProject;

	//The project a restart should come back to: the one being opened, or the one asked for.
	static string RestartProject => startProject ?? requested;

	static bool ProblemsUp => problems is not null && problems.IsOpen;

	//Puts a card up, taking any other one down first.
	static void ShowProblem( ProblemSheet card, ProblemKind kind )
	{
		//A port has no window, and nobody to read it.
		if ( !EditorMainWindow.Current.IsValid() ) return;

		if ( ProblemsUp ) problems.Close();

		problems = ProblemSheet.Show( card );
		problemKind = problems is null ? ProblemKind.None : kind;
	}

	//The editor's own projects did not build.
	static void ShowBuiltInsFailed()
	{
		var card = new ProblemSheet
		{
			Icon = "build",
			Title = "The editor's own code did not build",
			Message = "The editor compiles its built-in projects - base, tools and the editor's addons - as it starts, " +
				"and these errors stopped that build. Docks, inspectors and tools from them will be missing until they build. " +
				"Fix them, then restart the editor to pick the changes up.",
		};

		card.SetProblems( CompileProblems() );
		card.Choices.Add( new ProblemSheet.Choice( "Continue anyway", null ) );
		card.Choices.Add( new ProblemSheet.Choice( "Restart editor", () => EditorUtility.RestartEditor( RestartProject ), Primary: true ) );

		ShowProblem( card, ProblemKind.BuiltIns );
	}

	//Loading the editor's own projects threw.
	static void ShowBuiltInsCrashed( Exception e )
	{
		var card = new ProblemSheet
		{
			Icon = "build",
			Title = "The editor's own projects could not be loaded",
			Message = "Loading the built-in projects threw, so what they hold will be missing. " +
				"Restart the editor once whatever is below is fixed.",
			Details = e?.ToString(),
		};

		card.Choices.Add( new ProblemSheet.Choice( "Continue anyway", null ) );
		card.Choices.Add( new ProblemSheet.Choice( "Restart editor", () => EditorUtility.RestartEditor( RestartProject ), Primary: true ) );

		ShowProblem( card, ProblemKind.Other );
	}

	//The project opened, and its code did not build.
	static void ShowProjectFailed()
	{
		//The editor's own projects failing is the likelier cause, and its card already lists every error in the build - this one included.
		if ( ProblemsUp )
		{
			problems.SetProblems( CompileProblems() );
			return;
		}

		var name = Project.Current?.Config?.Title;
		if ( string.IsNullOrWhiteSpace( name ) ) name = "The project";

		var card = new ProblemSheet
		{
			Icon = "code_off",
			Title = $"{name} did not build",
			Message = "The project is open without its code, so its components are placeholders for now. " +
				"Fix the errors and recompile - saving a file recompiles it too - and this goes away by itself once it builds.",
		};

		card.SetProblems( CompileProblems() );
		card.Choices.Add( new ProblemSheet.Choice( "Continue without code", null ) );
		card.Choices.Add( new ProblemSheet.Choice( "Recompile", () => _ = Recompile(), Primary: true, KeepOpen: true ) );

		ShowProblem( card, ProblemKind.ProjectCode );
	}

	//A project that could not be opened. There is no closing half of one, so both ways past it start the editor again.
	static void ShowOpenFailed( string path, Exception e )
	{
		var name = System.IO.Path.GetFileNameWithoutExtension( path );

		var card = new ProblemSheet
		{
			Icon = "folder_off",
			Title = $"{name} could not be opened",
			Message = "Opening it stopped part way, and a half-open project cannot be taken back out of this editor, so it starts over. " +
				"Try again opens the same project in a fresh editor; the launcher lets you pick another.",
			Details = e?.ToString() ?? "Nothing was thrown - the log says what stopped it.",
		};

		card.Choices.Add( new ProblemSheet.Choice( "Back to launcher", () => EditorUtility.Quit( toLauncher: true ) ) );
		card.Choices.Add( new ProblemSheet.Choice( "Try again", () => EditorUtility.RestartEditor( path ), Primary: true ) );

		ShowProblem( card, ProblemKind.Other );

		//Nowhere to show it: the way it always went.
		if ( !ProblemsUp ) EditorUtility.Quit( toLauncher: true );
	}

	//Builds again what did not build, and waits for it. The card says so while it does, and takes the answer from the compile's own event.
	static async Task Recompile()
	{
		problems?.SetBusy( "Compiling…" );

		try
		{
			//Marked by hand: a build that failed is not in the list of things to build again
			//until a file of it changes, and the point of the button is not to have to touch one.
			foreach ( var compiler in Project.CompileGroup.Compilers )
			{
				if ( compiler.Diagnostics.Any( x => x.Severity == DiagnosticSeverity.Error ) )
					compiler.MarkForRecompile();
			}

			await Project.CompileAsync();
		}
		catch ( Exception e )
		{
			Log.Error( e, "the recompile threw" );
		}
		finally
		{
			problems?.SetBusy( null );
		}

		RefreshProblems();
	}

	[Event( "compile.complete" )]
	static void OnProblemsCompileComplete( Rinten.CompileGroup group )
	{
		if ( !ThreadSafe.IsMainThread )
		{
			MainThread.Queue( () => OnProblemsCompileComplete( group ) );
			return;
		}

		RefreshProblems();
	}

	//A build finished: the card lists what is left, or says it is over.
	static void RefreshProblems()
	{
		if ( !ProblemsUp ) return;

		if ( problemKind is not ( ProblemKind.BuiltIns or ProblemKind.ProjectCode ) ) return;

		//Still going: the next one will say.
		if ( Project.CompileGroup.IsBuilding ) return;

		var left = CompileProblems();

		if ( left.Count > 0 )
		{
			problems.SetProblems( left );
			return;
		}

		if ( problemKind == ProblemKind.ProjectCode )
		{
			Log.Info( "the project's code builds now" );

			problems.Close();
			problems = null;
			problemKind = ProblemKind.None;
			return;
		}

		//The editor's own code builds now, but what did not build the first time was left out of the docks made since - a restart puts it in.
		problems.SetProblems( null );
		problems.Say( "The editor's own code builds now",
			"Everything compiles. Restart the editor to load what was left out while it did not.", "check_circle" );
	}

	//The errors of the last build, a line each, with the project they are in.
	static List<ProblemSheet.Problem> CompileProblems()
	{
		var list = new List<ProblemSheet.Problem>();

		foreach ( var compiler in Project.CompileGroup.Compilers )
		{
			foreach ( var diagnostic in compiler.Diagnostics )
			{
				if ( diagnostic.Severity != DiagnosticSeverity.Error ) continue;

				list.Add( Of( diagnostic, compiler.AssemblyName ) );
			}
		}

		//A build that stopped before any compiler said anything keeps what it has on the group.
		if ( list.Count == 0 )
		{
			foreach ( var diagnostic in Project.GetCompileDiagnostics() )
			{
				if ( diagnostic.Severity == DiagnosticSeverity.Error )
					list.Add( Of( diagnostic, null ) );
			}
		}

		return list;
	}

	static ProblemSheet.Problem Of( Diagnostic diagnostic, string owner )
	{
		var span = diagnostic.Location.GetLineSpan();
		var mapped = diagnostic.Location.GetMappedLineSpan();

		//A diagnostic about no file at all - a missing reference - points nowhere.
		var file = mapped.HasMappedPath ? mapped.Path : span.Path;
		if ( string.IsNullOrWhiteSpace( file ) ) file = null;

		var start = ( mapped.HasMappedPath ? mapped : span ).StartLinePosition;

		return new ProblemSheet.Problem(
			diagnostic.GetMessage(),
			diagnostic.Id,
			owner,
			file,
			file is null ? 0 : start.Line + 1,
			file is null ? 0 : start.Character + 1 );
	}
}
