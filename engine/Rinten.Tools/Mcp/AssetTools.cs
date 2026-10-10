using System;
using System.IO;

namespace Editor.Mcp;

/// <summary>The asset system's list of files, told about files it hasn't walked past yet. The rest of the asset tools are the addon's; this one is the engine's because it is the asset system's own registration, wanted the moment a file is written.</summary>
[McpToolset( "asset" )]
internal static class AssetTools
{
	[McpTool( "asset_rescan" )]
	[Description( "Tell the asset system about files just written to the project - one file, or every file under a folder - so they can be opened right away. The file watcher notices new files on its own, but takes its time about it: open_scene on a scene a generator just wrote says 'No scene at' for several seconds. Call this after writing assets from outside the editor, then open or search them. Paths are absolute, or relative to the project's Assets folder." )]
	public static string AssetRescan(
		[Description( "A file or a folder, absolute or relative to the project's Assets folder." )] string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new Exception( "Give a file or folder path." );

		var content = FileSystem.Content
			?? throw new Exception( "No project is open - there's no asset folder to scan." );

		var absolute = Path.IsPathRooted( path ) ? path : content.GetFullPath( path.Replace( '\\', '/' ).TrimStart( '/' ) );

		if ( File.Exists( absolute ) )
		{
			var asset = AssetSystem.RegisterFile( absolute );

			return asset is null
				? $"Registered {absolute}, which is not an asset of its own (a compiled file beside its source, or a file of no asset type)."
				: $"Registered {asset.Path} ({asset.AssetType?.FriendlyName})";
		}

		if ( Directory.Exists( absolute ) )
		{
			var files = Directory.EnumerateFiles( absolute, "*", SearchOption.AllDirectories )
				.Where( x => !x.Contains( $"{Path.DirectorySeparatorChar}.", StringComparison.Ordinal ) )
				.ToArray();

			if ( files.Length == 0 )
				return $"Nothing under {absolute}.";

			AssetSystem.RegisterFiles( files );

			return $"Registered {files.Length} files under {absolute}. asset_search finds them now.";
		}

		throw new Exception( $"Nothing at {absolute}. Paths are absolute, or relative to {content.GetFullPath( "/" )}." );
	}
}
