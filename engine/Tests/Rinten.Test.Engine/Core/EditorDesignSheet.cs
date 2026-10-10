using Editor;
using Rinten.UI;
using System.IO;
using System.Linq;
using System.Reflection;

namespace EngineTests;

//Editor stylesheets parse, since a failed sheet draws nothing; shares global theme state, no parallel
[TestClass]
[DoNotParallelize]
public class EditorDesignSheetTests
{
	//The design sheet, off the disk, the same one the editor reads.
	private static string Read()
	{
		var game = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		var path = Path.Combine( game, EditorDesign.Path.TrimStart( '/' ) );

		Assert.IsTrue( File.Exists( path ), $"the design sheet is not at {path}" );

		return File.ReadAllText( path );
	}

	//A sheet that ships inside an assembly, read the way the editor reads it.
	private static string Embedded( Assembly assembly, string name )
	{
		var found = assembly.GetManifestResourceNames()
			.FirstOrDefault( x => x.EndsWith( name, System.StringComparison.OrdinalIgnoreCase ) );

		Assert.IsNotNull( found, $"{name} is not in {assembly.GetName().Name}" );

		using var stream = assembly.GetManifestResourceStream( found );
		using var reader = new StreamReader( stream );

		return reader.ReadToEnd();
	}

	//Parsed the way the editor parses it: the design tokens in front, then what the theme has an opinion about, then the sheet. Not through
	//StyleSheet.FromString : that one swallows the exception and hands back an empty sheet, which is exactly the failure this is here to catch.
	private static StyleSheet Parse( string text, string name, bool withTheme = true )
	{
		if ( withTheme )
		{
			EditorSheetPreamble.Register();
			return StyleParser.ParseSheet(
				EditorTokens.InFrontOf( text, EditorSheetPreamble.ThemeText() ),
				name );
		}

		return StyleParser.ParseSheet(
			EditorTokens.InFrontOf( text ),
			name );
	}

	[TestMethod]
	public void TheDesignSheetParses()
	{
		var sheet = Parse( Read(), EditorDesign.Path );

		Assert.IsNotNull( sheet );
		Assert.IsTrue( sheet.Nodes.Count > 0, "the sheet parsed to no rules at all" );
	}

	//The tokens are in the engine and go in front of every editor sheet, so a mistake in them is every sheet at once.
	[TestMethod]
	public void TheTokensAreThereAndParse()
	{
		Assert.IsFalse( string.IsNullOrWhiteSpace( EditorTokens.Text ), "the design tokens are not in the assembly" );

		var sheet = StyleParser.ParseSheet( EditorTokens.Text, EditorTokens.Name );

		Assert.IsNotNull( sheet );
	}

	//The sheets that travel inside an assembly, because the windows they dress are open before there is a filesystem to find them in.
	[TestMethod]
	public void TheEmbeddedSheetsParse()
	{
		//The engine's own windows have no theme: they are open before there is one, and use the fallback values in the tokens.
		Parse( Embedded( typeof( Rinten.UI.Panel ).Assembly, "editor.scss" ), "editor.scss", withTheme: false );

		//Every sheet the editor's own panels carry, rather than a list of them that is out of date the moment somebody writes another one.
		var tools = typeof( Theme ).Assembly;

		var sheets = tools.GetManifestResourceNames()
			.Where( x => x.EndsWith( ".scss", System.StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		Assert.IsTrue( sheets.Length > 0, "none of the editor's own sheets are in Rinten.Tools" );

		foreach ( var name in sheets )
		{
			var sheet = Parse( Embedded( tools, name ), name );

			Assert.IsTrue( sheet.Nodes.Count > 0, $"{name} parsed to no rules" );
		}
	}

	//The launcher's sheet, which is in a binary nothing here references - so it is read off the disk, from the
	//source it is built out of. That way a sheet that no longer parses says so before it is built into anything.
	[TestMethod]
	public void TheLauncherSheetParses()
	{
		var game = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		var path = Path.Combine( game, "..", "engine", "Launcher", "Rinten", "UI", "LauncherPanel.razor.scss" );

		//Somewhere else entirely - a game folder unpacked away from its source. Nothing to say about a file that is not there.
		if ( !File.Exists( path ) ) return;

		Parse( File.ReadAllText( path ), "LauncherPanel.razor.scss" );
	}

	//The theme file, off the disk, the same one the editor reads.
	private static string ReadTheme()
	{
		var game = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		var path = Path.Combine( game, EditorDesign.ThemePath.TrimStart( '/' ) );

		Assert.IsTrue( File.Exists( path ), $"the theme sheet is not at {path}" );

		return File.ReadAllText( path );
	}

	//Every $name: a sheet declares, in the order it declares them.
	private static string[] Declared( string sheet ) => sheet
		.Split( '\n' )
		.Select( x => x.Trim() )
		.Where( x => x.StartsWith( '$' ) && x.Contains( ':' ) )
		.Select( x => x[..x.IndexOf( ':' )].Trim() )
		.ToArray();

	//The theme is variables and nothing else. It is pasted in front of every editor sheet as that sheet parses,
	//so a rule in it would be parsed once per sheet - a hundred and fifty copies of the same rule, all matching.
	[TestMethod]
	public void TheThemeDeclaresNoRules()
	{
		foreach ( var line in ReadTheme().Split( '\n' ) )
		{
			var text = line.Trim();

			if ( text.Length == 0 || text.StartsWith( "//" ) || text.StartsWith( '$' ) ) continue;

			Assert.Fail( $"the theme is variables only, and this is not one: {text}" );
		}
	}

	//The colours sheets name come from the theme file, not from C#. Theme.cs is what is left of the widget editor and has nothing to say about a panel.
	[TestMethod]
	public void TheThemeIsWhereTheColoursComeFrom()
	{
		var declared = Declared( ReadTheme() ).ToHashSet();

		foreach ( var name in new[] { "$blue", "$surface-background", "$text-light", "$control-radius", "$row-height", "$font-mono" } )
		{
			Assert.IsTrue( declared.Contains( name ), $"{name} is named by sheets and the theme does not declare it" );
		}
	}

	//A name the theme and the tokens both declare is settled by the theme, which is why the theme is
	//read second. The tokens' own value is the fallback for a window opened before there is a theme.
	[TestMethod]
	public void TheThemeIsReadAfterTheTokens()
	{
		var theme = ReadTheme();
		var whole = EditorTokens.InFrontOf( "x { color: $accent; }", theme );
		var themeStarts = whole.IndexOf( theme, System.StringComparison.Ordinal );

		Assert.IsTrue( themeStarts > 0, "the theme did not make it into the preamble, or it went in first" );

		var shared = Declared( theme ).Intersect( Declared( EditorTokens.Text ) ).ToArray();

		Assert.IsTrue( shared.Length > 0, "the theme settles none of the tokens, which cannot be right" );

		//The parser substitutes as it reads, so whichever is written last wins.
		foreach ( var name in shared )
		{
			Assert.IsTrue( whole.LastIndexOf( $"{name}:", System.StringComparison.Ordinal ) >= themeStarts,
				$"{name} is in both sheets and the tokens have the last word on it" );
		}
	}

	//Black page with a grey plate under working docks; both halves are checked
	[TestMethod]
	public void EveryDockIsThePanelAndTheFrameIsTheHeader()
	{
		var sheet = Parse( Read(), EditorDesign.Path );

		//The whole selector, not a piece of one: a sheet has a dozen rules for what is inside a
		//bar and only one for the bar, and "contains" finds whichever of them was written first.
		StyleBlock Rule( string name ) => sheet.Nodes
			.FirstOrDefault( x => x.SelectorStrings.Any( y => y.Trim() == $".{name}" ) );

		var busy = Rule( "inspector" );
		var page = Rule( "captioned" );
		var dock = Rule( "dockwidget" );
		var bar = Rule( "viewport-toolbar" );
		var tabs = Rule( "tabbar" );

		Assert.IsNotNull( busy, "nothing in the sheet dresses the inspector" );
		Assert.IsNotNull( page, "nothing in the sheet fills the window itself" );
		Assert.IsNotNull( dock, "nothing in the sheet dresses a dock" );
		Assert.IsNotNull( bar, "nothing in the sheet dresses the viewport's bar" );
		Assert.IsNotNull( tabs, "nothing in the sheet dresses the strip of open scenes" );

		var panel = dock.Styles.BackgroundColor;
		var header = page.Styles.BackgroundColor;

		Assert.IsTrue( panel.HasValue, "a dock has no ground of its own" );
		Assert.IsTrue( header.HasValue, "the window has no ground of its own" );

		Assert.AreEqual( 1.0f, panel.Value.a, 0.001f, "a dock's ground is see-through" );

		Assert.IsTrue( panel.Value.r > header.Value.r,
			$"a dock ({panel.Value.Hex}) is no lighter than the header around it ({header.Value.Hex})" );

		//One grey under everything that is not the frame: the inspector and the bar of controls over a viewport are the same panel as the dock they stand in.
		Assert.AreEqual( panel.Value.Hex, busy.Styles.BackgroundColor?.Hex,
			"the inspector is not the same panel as its dock" );
		Assert.AreEqual( panel.Value.Hex, bar.Styles.BackgroundColor?.Hex,
			"the viewport's bar of controls is not the same panel as its dock" );

		//And the strip of open scenes is a header like the strip of tabs over a group of docks: the window's own dark, not the panel it opens.
		Assert.AreEqual( header.Value.Hex, tabs.Styles.BackgroundColor?.Hex,
			"the strip of open scenes is not the same dark as the header" );
	}

	//One accent, and no band behind the names in a tab strip. Read off the disk rather than through Theme: a test host has no
	//mounted content, so what the theme is holding are its fallbacks and not what is in the file the editor actually reads.
	[TestMethod]
	public void TheThemeSaysOneAccentAndNoBandBehindTheTabs()
	{
		var game = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		var path = Path.Combine( game, "addons/tools/assets/styles/theme.json" );

		Assert.IsTrue( File.Exists( path ), $"the theme is not at {path}" );

		var theme = System.Text.Json.JsonDocument.Parse( File.ReadAllText( path ) ).RootElement;

		string Value( string name ) => theme.GetProperty( name ).GetString();

		//Prefab and TextLink share the accent colour; Blue is intentionally a separate colour now
		foreach ( var name in new[] { "Prefab", "TextLink" } )
		{
			Assert.AreEqual( Value( "Primary" ), Value( name ),
				$"{name} is not the accent, so the editor has two of them" );
		}

		//The strip a tab sits in belongs to the window. A second darkness across it is a grey band the panels below it do not explain.
		foreach ( var name in new[] { "TabBarBackground", "TabInactiveBackground" } )
		{
			var colour = Color.Parse( Value( name ) );

			Assert.IsTrue( colour.HasValue, $"{name} is not a colour" );
			Assert.AreEqual( 0.0f, colour.Value.a, 0.001f, $"{name} paints a band behind the tabs" );
		}
	}

	[TestMethod]
	public void TheThingsTheEditorIsMadeOfAreInIt()
	{
		var sheet = Parse( Read(), EditorDesign.Path );

		var selectors = sheet.Nodes
			.SelectMany( x => x.SelectorStrings )
			.ToArray();

		//The bricks the razor panels are written against. A rename on one side and not the other is a control that quietly loses its look.
		foreach ( var name in new[] { "toolbarbutton", "toolbarreadout", "widgetpanel", "viewport-toolbar", "editor-overlay", "tooltip", "menu" } )
		{
			Assert.IsTrue( selectors.Any( x => x.Contains( name ) ), $"nothing in the sheet dresses .{name}" );
		}

		Assert.IsFalse( Read().Contains( "blue_noise" ),
			"a vtex as a window fill is magenta when the UI cannot load it" );
	}

	[TestMethod]
	public void TheInspectorPanelSheetsParse()
	{
		var game = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		foreach ( var relative in new[]
		{
			"addons/tools/Code/Editor/InspectorShellPanel.razor.scss",
			"addons/tools/Code/Scene/GameObjectInspector/GameObjectInspectorPanel.razor.scss",
			"addons/tools/Code/Editor/Inspectors/ControlSheetRow.razor.scss",
			"addons/tools/Code/Scene/GameObjectInspector/GameObjectHeaderPanel.razor.scss",
			"addons/tools/Code/Scene/GameObjectInspector/ComponentSheetHeaderPanel.cs.scss",
		} )
		{
			var path = Path.Combine( game, relative );
			Assert.IsTrue( File.Exists( path ), $"inspector sheet is not at {path}" );

			var sheet = Parse( File.ReadAllText( path ), relative );
			Assert.IsTrue( sheet.Nodes.Count > 0, $"{relative} parsed to no rules" );
		}
	}
}
