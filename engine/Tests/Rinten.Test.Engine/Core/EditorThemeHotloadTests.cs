using Editor;
using Rinten;
using Rinten.Engine;
using System;
using System.IO;
using System.Threading.Tasks;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class EditorThemeHotloadTests
{
	private static string ThemeMountPath => EditorDesign.ThemePath;

	private static string GameFolder()
	{
		var engine = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		if ( !string.IsNullOrWhiteSpace( engine ) )
		{
			var game = Path.GetFullPath( Path.Combine( engine, "..", "game" ) );
			if ( File.Exists( Path.Combine( game, ThemeMountPath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) ) ) )
				return game;
		}

		var dir = AppContext.BaseDirectory;
		while ( !string.IsNullOrEmpty( dir ) )
		{
			var game = Path.Combine( dir, "game" );
			if ( File.Exists( Path.Combine( game, ThemeMountPath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) ) ) )
				return game;
			dir = Path.GetDirectoryName( dir );
		}

		Assert.Fail( $"could not find game folder for {ThemeMountPath}" );
		return null;
	}

	[ClassInitialize]
	public static void ClassInitialize( TestContext context )
	{
		if ( EngineFileSystem.Root is null )
			EngineFileSystem.Initialize( GameFolder(), skipBaseFolderInit: true );

		EditorSheetPreamble.Register();
	}

	private static async Task<bool> TickUntilChangeDetected( Func<bool> changeDetected, float timeoutSeconds = 5f )
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();

		while ( sw.Elapsed.TotalSeconds < timeoutSeconds )
		{
			FileWatch.Tick();

			if ( changeDetected() )
				return true;

			await Task.Delay( 50 );
		}

		return false;
	}
	//A file of the tests' own in the watched folder, so a run that dies part way through leaves junk rather than a source file of the editor.
	private static string Probe( string name ) => $"/addons/tools/assets/styles/hotload-{name}.scss";

	private static void WriteProbe( string path, string text )
	{
		Editor.FileSystem.Root.WriteAllText( path, text );
	}

	//Makes the probe and waits out the clock, so the change that follows lands on a later second than the file was made in.
	private static async Task SettleProbe( string path )
	{
		WriteProbe( path, "/* probe */" );

		for ( var i = 0; i < 14; i++ )
		{
			FileWatch.Tick();
			await Task.Delay( 100 );
		}
	}

	private static void RemoveProbe( string path )
	{
		var root = Editor.FileSystem.Root;

		if ( root is null || !root.FileExists( path ) ) return;

		try { root.DeleteFile( path ); } catch ( IOException ) { }
	}

	[TestMethod]
	public void WatchEditorStyles_DoesNotThrowWhenCalledTwice()
	{
		ManagedTools.WatchEditorStyles();
		ManagedTools.WatchEditorStyles();
	}

	[TestMethod]
	public async Task StylesFolderWatchDeliversThemeChange()
	{
		var probe = Probe( "watch" );

		var root = Editor.FileSystem.Root;
		Assert.IsNotNull( root, "editor filesystem root should be initialized for this test" );

		await SettleProbe( probe );

		var probeFile = Path.GetFileName( probe );
		bool detected = false;

		using var watcher = root.Watch( "/addons/tools/assets/styles/*" );
		watcher.OnChangedFile += path =>
		{
			if ( string.Equals( Path.GetFileName( path ), probeFile, StringComparison.OrdinalIgnoreCase ) )
				detected = true;
		};

		try
		{
			WriteProbe( probe, "/* watched */" );
			Assert.IsTrue( await TickUntilChangeDetected( () => detected ), "a styles file change should be watched" );
		}
		finally
		{
			RemoveProbe( probe );
		}
	}

	[TestMethod]
	public void ThemeTextReloadRereadsDisk()
	{
		var probe = Probe( "reload" );

		var root = Editor.FileSystem.Root;
		Assert.IsNotNull( root );

		const string marker = "/* editor-theme-hotload-test */";
		var was = EditorSheetPreamble.ThemePath;

		try
		{
			WriteProbe( probe, marker );

			EditorSheetPreamble.ThemePath = probe;
			EditorSheetPreamble.Reload();

			Assert.IsTrue( EditorSheetPreamble.ThemeText().Contains( marker ), "Reload should pick up the theme from disk" );
		}
		finally
		{
			EditorSheetPreamble.ThemePath = was;
			RemoveProbe( probe );
			EditorSheetPreamble.Reload();
		}
	}

	[TestMethod]
	public async Task WatchEditorStylesTriggersReloadAll()
	{
		var probe = Probe( "reloadall" );

		var root = Editor.FileSystem.Root;
		Assert.IsNotNull( root );

		ManagedTools.WatchEditorStyles();

		await SettleProbe( probe );

		var countBefore = EditorSheetPreamble.ReloadAllCount;

		try
		{
			WriteProbe( probe, "/* saved */" );
			Assert.IsTrue(
				await TickUntilChangeDetected( () => EditorSheetPreamble.ReloadAllCount > countBefore ),
				"a styles save should trigger ReloadAll via the styles watcher" );
		}
		finally
		{
			RemoveProbe( probe );
			EditorSheetPreamble.Reload();
		}
	}
}
