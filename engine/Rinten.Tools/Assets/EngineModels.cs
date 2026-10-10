using System;
using System.IO;

namespace Rinten;

//The models the engine ships, built before it ships (-bakemodels) so a first project does not build them
internal static class EngineModels
{
	//Every .mdl under <root>/addons/*/Assets, built beside itself; false if any failed
	internal static bool BakeAll( string root )
	{
		var addons = Path.Combine( root, "addons" );
		if ( !Directory.Exists( addons ) )
		{
			Log.Error( $"no {addons} to build models in" );
			return false;
		}

		var built = 0;
		var failed = 0;

		foreach ( var file in Directory.EnumerateFiles( addons, "*.mdl", SearchOption.AllDirectories ) )
		{
			if ( !file.Replace( '\\', '/' ).Contains( "/Assets/", StringComparison.Ordinal ) ) continue;

			if ( ModelDefinitionCompiler.CompileToFile( file ) )
			{
				built++;
				continue;
			}

			failed++;
			Log.Error( $"{file}: did not build" );
		}

		Log.Info( $"engine models: {built} built, {failed} failed" );
		return failed == 0;
	}
}
