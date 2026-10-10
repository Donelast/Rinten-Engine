using Rinten;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.Mcp;

[McpToolset( "export", "Building the open project into a game somebody else can run" )]
public static partial class ExportTools
{
	/// <summary>Build the open project for a platform into a folder - what Project Settings → Build Project does, with the project's own build settings. Returns where it was written and what is wrong with it; the details of the build are in the console. Only once the project has finished loading: a build asked for while its assets are still being found carries the ones found so far.</summary>
	/// <param name="platform">Which platform, by the name list_export_platforms gives it - 'web', for one.</param>
	/// <param name="folder">The folder to write the build into. Made if it is not there.</param>
	[McpTool( "export_game" )]
	public static async Task<ExportResult> ExportGame( string platform, string folder )
	{
		var project = Project.Current ?? throw new Exception( "No project is open" );
		var exporter = await StandaloneExporter.ForProject( project, platform, folder );

		await exporter.Run();

		return new ExportResult
		{
			Written = exporter.Result ?? folder,
			Problems = exporter.Problems.ToArray(),
		};
	}

	/// <summary>The platforms export_game can build for.</summary>
	[McpTool.ReadOnly( "list_export_platforms" )]
	public static string[] ListExportPlatforms() => ExportPlatform.Offered.Select( x => $"{x.Folder} - {x.Title}" ).ToArray();

	/// <summary>What a build came out as.</summary>
	public class ExportResult
	{
		/// <summary>Where it was written: the folder, or the package it became beside it.</summary>
		public string Written { get; set; }

		/// <summary>What would stop it running, where anything would. Empty when nothing.</summary>
		public string[] Problems { get; set; }
	}
}
