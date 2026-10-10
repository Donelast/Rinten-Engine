using System.IO;

namespace Rinten;

//Finding the export an asset definition was authored from. Shared by the compilers rather than written out in each: a model and the
//clips that play on it name their files the same way, and a search order that differed between them would find one and not the other.
internal static class AssetSources
{
	/// <summary>Where a file named by a definition actually is, or null.</summary>
	public static string Resolve( string file, string sourceDirectory )
	{
		if ( string.IsNullOrWhiteSpace( file ) ) return null;

		if ( Path.IsPathRooted( file ) )
			return File.Exists( file ) ? file : null;

		//Beside the definition first - the usual case, and how an artist thinks about it - then as a project path.
		if ( !string.IsNullOrEmpty( sourceDirectory ) )
		{
			var beside = Path.Combine( sourceDirectory, file );
			if ( File.Exists( beside ) ) return beside;
		}

		foreach ( var system in new[] { EngineFileSystem.Mounted, EngineFileSystem.Assets } )
		{
			if ( system is null || !system.FileExists( file ) ) continue;

			var full = system.GetFullPath( file );
			if ( !string.IsNullOrEmpty( full ) && File.Exists( full ) ) return full;
		}

		return null;
	}
}
