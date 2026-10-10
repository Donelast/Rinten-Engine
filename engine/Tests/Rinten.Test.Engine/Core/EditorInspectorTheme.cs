using Editor;
using Rinten;
using Rinten.UI;
using System;
using System.IO;

namespace EngineTests;

[TestClass]
public class EditorInspectorThemeTests
{
	private static string GameFolder()
	{
		var engine = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		if ( !string.IsNullOrWhiteSpace( engine ) )
		{
			var game = Path.GetFullPath( Path.Combine( engine, "..", "game" ) );
			var path = Path.Combine( game, EditorDesign.ThemePath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) );
			if ( File.Exists( path ) ) return game;
		}

		var dir = AppContext.BaseDirectory;
		while ( !string.IsNullOrEmpty( dir ) )
		{
			var game = Path.Combine( dir, "game" );
			var path = Path.Combine( game, EditorDesign.ThemePath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) );
			if ( File.Exists( path ) ) return game;
			dir = Path.GetDirectoryName( dir );
		}

		Assert.Fail( $"could not find game folder for {EditorDesign.ThemePath}" );
		return null;
	}

	private static string ReadTheme()
	{
		var path = Path.Combine( GameFolder(), EditorDesign.ThemePath.TrimStart( '/' ).Replace( '/', Path.DirectorySeparatorChar ) );
		return File.ReadAllText( path );
	}

	private static StyleSheet ParseTheme( string themeText )
	{
		return StyleParser.ParseSheet(
			EditorTokens.InFrontOf( ".test-theme { color: $surface-background; }", themeText ),
			EditorDesign.ThemePath );
	}

	[TestMethod]
	public void ThemeSheetParses()
	{
		var sheet = ParseTheme( ReadTheme() );
		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Count > 0 );
	}

	[TestMethod]
	public void ThemeDefinesSurfaceBackground()
	{
		var text = ReadTheme();
		Assert.IsTrue( text.Contains( "$surface-background" ), "theme.scss should define $surface-background" );
	}
}
