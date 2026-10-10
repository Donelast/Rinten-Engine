using Editor;
using Rinten;
using Rinten.UI;
using System;
using System.IO;

namespace EngineTests;

//Reload and ReloadAll below throw away the theme text and the prepared-path set that every other sheet test reads, so these cannot run beside them.
[TestClass]
[DoNotParallelize]
public class EditorSheetPreambleTests
{
	private static string GameFolder()
	{
		var engine = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		if ( !string.IsNullOrWhiteSpace( engine ) )
		{
			var game = Path.GetFullPath( Path.Combine( engine, "..", "game" ) );
			if ( File.Exists( Path.Combine( game, EditorDesign.ThemePath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) ) ) )
				return game;
		}

		var dir = AppContext.BaseDirectory;
		while ( !string.IsNullOrEmpty( dir ) )
		{
			var game = Path.Combine( dir, "game" );
			if ( File.Exists( Path.Combine( game, EditorDesign.ThemePath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) ) ) )
				return game;
			dir = Path.GetDirectoryName( dir );
		}

		Assert.Fail( $"could not find game folder for {EditorDesign.ThemePath}" );
		return null;
	}

	private static string GamePath( string mountPath ) =>
		Path.Combine( GameFolder(), mountPath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) );

	[TestMethod]
	public void ToolsPanelSheetParsesWithoutImport()
	{
		var path = "/addons/tools/Code/Editor/Inspectors/ControlSheetRow.razor.scss";
		var text = File.ReadAllText( GamePath( path ) );

		EditorSheetPreamble.Register();

		var sheet = EditorSheetPreamble.ParseSheet( text, path );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Count > 0, "ControlSheetRow should parse with auto theme" );
	}

	[TestMethod]
	public void ThemeVariablesResolveInToolsPanelSheet()
	{
		var path = "/addons/tools/Code/Editor/Inspectors/ControlSheetRow.razor.scss";
		var text = File.ReadAllText( GamePath( path ) );

		EditorSheetPreamble.Register();

		var sheet = StyleParser.ParseSheet(
			EditorTokens.PrepareSheetText( text, path ),
			path );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Count > 0 );
	}

	[TestMethod]
	public void ToolsPathWithoutLeadingSlashIsPrepared()
	{
		var path = "addons/tools/Code/Editor/Inspectors/ControlSheetRow.razor.scss";
		Assert.IsTrue( EditorSheetPreamble.ShouldPreparePath( path ) );
	}

	[TestMethod]
	public void ToolsVirtualMountPathsArePrepared()
	{
		Assert.IsTrue( EditorSheetPreamble.ShouldPreparePath( "/editor/inspectors/controlsheetrow.razor.scss" ) );
		Assert.IsTrue( EditorSheetPreamble.ShouldPreparePath( "editor/inspectors/controls/stringcontrol.razor.scss" ) );
		Assert.IsTrue( EditorSheetPreamble.ShouldPreparePath( "/scene/gameobjectinspector/tagsetcontrol.razor.scss" ) );
		Assert.IsTrue( EditorSheetPreamble.ShouldPreparePath( "/audio/mixertree/mixerrowpanel.scss" ) );
		Assert.IsTrue( EditorSheetPreamble.ShouldPreparePath( "audio/mixerdetailpanel.razor.scss" ) );
	}

	[TestMethod]
	public void VirtualMountSheetParsesWithoutImport()
	{
		var path = "/editor/inspectors/controlsheetrow.razor.scss";
		var diskPath = "/addons/tools/Code/Editor/Inspectors/ControlSheetRow.razor.scss";
		var text = File.ReadAllText( GamePath( diskPath ) );

		EditorSheetPreamble.Register();

		var sheet = EditorSheetPreamble.ParseSheet( text, path );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Count > 0, "virtual mount path should get theme preamble" );
	}

	[TestMethod]
	public void VirtualMountAudioSheetParsesWithoutImport()
	{
		var path = "/audio/mixertree/mixertreerow.scss";
		var diskPath = "/addons/tools/Code/Audio/MixerTree/MixerTreeRow.cs.scss";
		var text = File.ReadAllText( GamePath( diskPath ) );

		EditorSheetPreamble.Register();

		var sheet = EditorSheetPreamble.ParseSheet( text, path );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Count > 0, "mixer row sheet should parse with theme preamble" );
	}

	[TestMethod]
	public void DesignSheetPathIsNotAutoPrepared()
	{
		Assert.IsFalse( EditorSheetPreamble.ShouldPreparePath( EditorDesign.Path ) );
		Assert.IsFalse( EditorSheetPreamble.ShouldPreparePath( EditorDesign.ThemePath ) );
	}

	[TestMethod]
	public void PrepareSheetTextDoesNotStackTokens()
	{
		EditorSheetPreamble.Register();

		var path = "/addons/tools/Code/Editor/Inspectors/ControlSheetRow.razor.scss";
		var once = EditorTokens.PrepareSheetText( "x { color: $ink-dim; }", path );
		var twice = EditorTokens.PrepareSheetText( once, path );

		Assert.AreEqual( once, twice );
	}

	[TestMethod]
	public void ReloadAllDoesNotThrowBeforeFilesystem()
	{
		EditorSheetPreamble.Reload();
		EditorSheetPreamble.ReloadAll();
	}

	[TestMethod]
	public void ThemeTextIncludesSurfaceBackgroundVariable()
	{
		var preamble = File.ReadAllText( GamePath( EditorDesign.ThemePath ) );

		Assert.IsTrue( preamble.Contains( "$surface-background" ), "theme preamble should carry theme.scss variables" );
	}
}
