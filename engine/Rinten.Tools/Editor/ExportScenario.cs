using Rinten;

namespace Editor;

/// <summary>Builds the project from the command line and exits: <c>-export web -exportto &lt;folder&gt;</c>.</summary>
public static class ExportScenario
{
	private static bool started;

	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( started ) return;

		//Read here rather than in a static initialiser: a switch read before the command line is parsed is read as empty for the rest of the run.
		var platform = Rinten.Utility.CommandLine.GetSwitch( "-export", "" ).TrimQuoted();
		if ( string.IsNullOrWhiteSpace( platform ) )
		{
			started = true;
			return;
		}

		//The project is opened a frame or two after the editor, and a build asked for while its assets are still being found carries the ones found so far.
		if ( Project.Current is null || StartupLoadProject.IsLoading ) return;

		//And the models that open left to compile a frame at a time - see CompileOnOpen. A build started before they were done
		//shipped a model added since the last open as its .mdl alone, with no .mdl_c beside it, and a page cannot compile one.
		if ( CompileOnOpen.Pending > 0 ) return;

		started = true;

		var folder = Rinten.Utility.CommandLine.GetSwitch( "-exportto", "" ).TrimQuoted();
		_ = Run( platform, folder );
	}

	private static async Task Run( string platform, string folder )
	{
		var code = 0;

		try
		{
			Log.Info( $"-export {platform}: building {Project.Current.Config.Ident} into {folder}" );

			var exporter = await StandaloneExporter.ForProject( Project.Current, platform, folder );
			await exporter.Run();

			if ( exporter.Problems.Count > 0 )
			{
				code = 2;
				foreach ( var problem in exporter.Problems ) Log.Warning( $"-export: {problem}" );
			}

			Log.Info( $"-export {platform}: written to {exporter.Result ?? folder}" );
		}
		catch ( System.Exception e )
		{
			code = 1;
			Log.Error( e, $"-export {platform}: the build failed" );
		}

		System.Environment.Exit( code );
	}
}
