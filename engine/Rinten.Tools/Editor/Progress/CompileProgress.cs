using Microsoft.CodeAnalysis;
using Rinten;
using System;
using System.Linq;

namespace Editor;

//Reports compiles (hotloads) on the progress bar; a failed build turns red with the first error
internal static class CompileProgress
{
	//A group being watched, and the job it is reporting to.
	sealed class Watch
	{
		public CompileGroup Group;
		public EditorProgress.Job Job;
	}

	static readonly List<Watch> watching = new();

	//Whether the preferences have been reported once already.
	static bool said;

	//The file and line the bar's press opens, where a build failed.
	static string errorFile;
	static int errorLine;

	[Event( "compile.started" )]
	public static void OnCompileStarted( CompileGroup group )
	{
		if ( group is null ) return;

		//On the main thread, and so in the order they came in: the end of one build is taken before the start of the next - see OnCompileComplete.
		if ( !ThreadSafe.IsMainThread )
		{
			MainThread.Queue( () => OnCompileStarted( group ) );
			return;
		}

		if ( EditorPreferences.CompileNotifications == EditorPreferences.NotificationLevel.NeverShow
			|| !EditorPreferences.NotificationPopups )
		{
			//Said once rather than on every build: a bar that shows nothing because somebody
			//turned notifications off years ago looks exactly like a bar that is broken.
			if ( !said )
			{
				said = true;

				EditorProgress.Log.Info( $"compiles are not shown: CompileNotifications" +
					$"={EditorPreferences.CompileNotifications}, NotificationPopups={EditorPreferences.NotificationPopups}" );
			}

			return;
		}

		//At startup the loading job already covers compiling; failures are reported once the editor is up
		if ( StartupLoadProject.IsLoading ) return;

		//A group that is already being watched is the same build being reported twice - the startup runs the
		//event again once there is somewhere to show it. Pick the watch back up rather than starting a second one.
		var watch = watching.FirstOrDefault( x => x.Group == group );

		//Create the job before adding the watch, or Tick discards the jobless watch and it never finishes
		if ( watch is null )
		{
			var job = EditorProgress.Start( $"Compiling {group.Name}", "build_circle" );

			watch = new Watch { Group = group, Job = job };
			watching.Add( watch );
		}

		//Told to show only the ones that went wrong: it runs, silently, and the bar has it only if it fails.
		watch.Job.Muted = EditorPreferences.CompileNotifications == EditorPreferences.NotificationLevel.ShowOnError;

		Tick();
	}

	/// <summary>End the job as soon as the group reports a result; back-to-back builds never look idle</summary>
	[Event( "compile.complete" )]
	public static void OnCompileComplete( CompileGroup group )
	{
		if ( group is null ) return;

		if ( !ThreadSafe.IsMainThread )
		{
			MainThread.Queue( () => OnCompileComplete( group ) );
			return;
		}

		for ( int i = watching.Count - 1; i >= 0; i-- )
		{
			var watch = watching[i];
			if ( watch.Group != group ) continue;

			if ( watch.Job is not null ) Done( watch );
			watching.RemoveAt( i );
		}
	}

	//What the groups being watched are doing, every frame - and from the bar's own tick, because a compile at startup happens where frames do not run.
	internal static void Tick()
	{
		for ( int i = watching.Count - 1; i >= 0; i-- )
		{
			var watch = watching[i];

			if ( watch.Group is null || watch.Job is null )
			{
				//Nothing to end, and nothing to watch - but a job that is there is never let go of unfinished.
				watch.Job?.Finish( null, 0f );
				watching.RemoveAt( i );
				continue;
			}

			if ( watch.Group.IsBuilding )
			{
				Report( watch );
				continue;
			}

			Done( watch );
			watching.RemoveAt( i );
		}
	}

	//How far through the group is, while it builds.
	static void Report( Watch watch )
	{
		int building = 0;
		int done = 0;
		int warnings = 0;

		foreach ( var compiler in watch.Group.Compilers )
		{
			if ( compiler.IsBuilding ) building++;
			else done++;

			if ( compiler.Diagnostics is null ) continue;

			foreach ( var diagnostic in compiler.Diagnostics )
			{
				if ( diagnostic.Severity >= DiagnosticSeverity.Warning ) warnings++;
			}
		}

		var job = watch.Job;

		job.Fraction = building + done > 0 ? done / (float)(building + done) : -1f;
		job.Subtitle = warnings > 0
			? $"{building} pending, {done} done, {warnings} warnings"
			: $"{building} pending, {done} done";
	}

	//What a build that worked sounds like, and one that did not - in game/core, so every editor has them. They were success.wav
	//and fail.wav, which nothing ever shipped: PlayRawSound found no file and said nothing, and every build was silent.
	const string CompiledSound = "sounds/editor/compile_success.mp3";
	const string FailedSound = "sounds/editor/compile_error.mp3";

	//How long a failed build stays on the bar, red, before it goes.
	const float FailedLinger = 3f;

	//Success judged by this build's own result, not stale results of other compilers in the group
	static void Done( Watch watch )
	{
		var job = watch.Job;
		var result = watch.Group.BuildResult;

		if ( result.Success )
		{
			job.Finish( "built" );

			if ( EditorPreferences.NotificationSounds )
				EditorUtility.PlayRawSound( CompiledSound );

			return;
		}

		var errors = ( result.Diagnostics ?? new() )
			.Where( x => x.Severity == DiagnosticSeverity.Error )
			.ToArray();

		//A failed build is on the bar whatever the preference says: "show on error" is exactly this, and "always" already had it.
		job.Muted = false;
		job.Icon = "error";
		job.Fraction = -1f;

		var first = errors.FirstOrDefault();

		Locate( first );

		//Red for long enough to be read, then gone: the errors are in the console, and a bar that stays is one that looks stuck.
		job.Pressed = errorFile is null ? null : Open;
		job.Fail( first is null
			? $"{watch.Group.Name} did not build - the console has it"
			: $"{errors.Length} error(s) - {first.GetMessage()}", FailedLinger );

		if ( EditorPreferences.NotificationSounds )
			EditorUtility.PlayRawSound( FailedSound );
	}

	//Where the error is, for the press on the bar to open.
	static void Locate( Diagnostic diagnostic )
	{
		errorFile = null;
		errorLine = 0;

		if ( diagnostic is null ) return;

		var span = diagnostic.Location.GetLineSpan();
		var mapped = diagnostic.Location.GetMappedLineSpan();

		//A diagnostic that is about no file at all - a reference that is not there, a compiler option - has a span that points nowhere.
		errorFile = mapped.HasMappedPath ? mapped.Path : span.Path;
		errorLine = mapped.Span.Start.Line + 1;

		if ( string.IsNullOrWhiteSpace( errorFile ) ) errorFile = null;
	}

	static void Open()
	{
		if ( errorFile is null ) return;

		CodeEditor.OpenFile( errorFile, errorLine );
	}
}
