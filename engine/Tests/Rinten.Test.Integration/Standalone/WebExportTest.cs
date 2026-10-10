using Editor;
using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;

namespace StandaloneTests;

//Web export can't be run here, so check the folder has player, content list, manifest, code, archive
[TestClass]
public class WebExportTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		Project.Clear();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	[TestMethod]
	public async Task ExportForTheWeb()
	{
		var player = Path.Combine( Environment.CurrentDirectory, ExportPlatform.Web.BinFolder );

		if ( !File.Exists( Path.Combine( player, "index.html" ) ) )
		{
			Assert.Inconclusive( $"no web player in {player} - run engine/Core/build-web.sh" );
			return;
		}

		var config = new ExportConfig
		{
			Platform = ExportPlatform.Web,
			TargetDir = Path.Combine( Path.GetTempPath(), Guid.NewGuid().ToString(), "webexport" ),
		};

		try
		{
			var project = Project.AddFromFile( "unittest/addons/spacewars/.rinproj" );
			config.Project = project;
			config.ExecutableName = project.Package.Ident;

			var exporter = await StandaloneExporter.FromConfig( config );
			await exporter.Run();

			var target = config.TargetDir;

			Assert.IsTrue( File.Exists( Path.Combine( target, "index.html" ) ), "no page to open" );
			Assert.IsTrue( File.Exists( Path.Combine( target, "native.js" ) ), "no native binding" );
			Assert.IsTrue( Directory.Exists( Path.Combine( target, "_framework" ) ), "no .NET runtime" );

			var list = File.ReadAllLines( Path.Combine( target, WebBuild.AssetListName ) )
				.Where( x => !string.IsNullOrWhiteSpace( x ) )
				.ToArray();

			Assert.IsTrue( list.Length > 0, "the build lists no content" );

			foreach ( var relative in list )
			{
				Assert.IsTrue( File.Exists( Path.Combine( target, relative.Replace( '/', Path.DirectorySeparatorChar ) ) ),
					$"{relative} is listed and is not in the build" );
			}

			var manifest = JsonSerializer.Deserialize<WebManifest>(
				File.ReadAllText( Path.Combine( target, WebBuild.ManifestName ) ) );

			Assert.IsNotNull( manifest );
			Assert.AreEqual( project.Config.Title, manifest.Name );
			Assert.IsTrue( manifest.Assemblies.Count > 0, "the build has no game code in it" );

			foreach ( var assembly in manifest.Assemblies )
			{
				Assert.IsTrue( File.Exists( Path.Combine( target, assembly.Replace( '/', Path.DirectorySeparatorChar ) ) ),
					$"{assembly} is in the manifest and not in the build" );
			}

			//itch.io unpacks the archive and opens index.html at its root.
			var zip = Path.Combine( Path.GetDirectoryName( Path.GetFullPath( target ) )!, $"{config.ExecutableName}-web.zip" );

			Assert.IsTrue( File.Exists( zip ), "no archive to upload" );

			using var archive = ZipFile.OpenRead( zip );
			Assert.IsNotNull( archive.GetEntry( "index.html" ), "the archive has no page at its root" );
		}
		finally
		{
			CleanOutput( config.TargetDir );
		}
	}

	private static void CleanOutput( string target )
	{
		if ( target is null ) return;

		var folder = Path.GetDirectoryName( Path.GetFullPath( target ) );
		if ( folder is null || !Directory.Exists( folder ) ) return;

		try
		{
			Directory.Delete( folder, true );
		}
		catch ( IOException )
		{
			Thread.Sleep( 500 );
			try { Directory.Delete( folder, true ); } catch ( IOException ) { }
		}
	}
}
