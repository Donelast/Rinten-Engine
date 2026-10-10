using Editor;
using Rinten;
using Rinten.UI;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace EngineTests;

//Builds the real launcher panel from its shipped folder, testing the on-disk markup and styles
[TestClass]
[DoNotParallelize]
public class LauncherPanelTreeTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		Rinten.Engine.GlobalContext.Current.UISystem ??= new UISystem();

		//The launcher reads projects from Config at construction; mount Config separately, Root may exist
		if ( EngineFileSystem.Config is null )
		{
			var game = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

			if ( game is not null )
			{
				if ( EngineFileSystem.Root is null )
					EngineFileSystem.Initialize( game );

				EngineFileSystem.InitializeConfigFolder();
			}
		}
	}

	//A project on the disk and in the list, for as long as it is held. The launcher shows the list this machine happens to have, and a machine
	//that has never opened anything has none - so the test that asks what a project looks like puts one there itself, and puts back what it found.
	private sealed class AProjectInTheList : IDisposable
	{
		private readonly string folder;
		private readonly string before;

		public AProjectInTheList()
		{
			folder = Path.Combine( Path.GetTempPath(), $"rinten-launcher-{Guid.NewGuid():N}" );

			Directory.CreateDirectory( folder );

			var file = Path.Combine( folder, $"test_project{Project.Extension}" );

			File.WriteAllText( file,
				"""{ "Title": "Test Project", "Type": "game", "Org": "local", "Ident": "test_project", "Schema": 1 }""" );

			before = EngineFileSystem.Config.FileExists( "/addons.json" )
				? EngineFileSystem.Config.ReadAllText( "/addons.json" )
				: null;

			var list = new ProjectList();

			Assert.IsNotNull( list.TryAddFromFile( file ), $"the test's own project does not load: {file}" );

			list.SaveList();
		}

		public void Dispose()
		{
			if ( before is null ) EngineFileSystem.Config.DeleteFile( "/addons.json" );
			else EngineFileSystem.Config.WriteAllText( "/addons.json", before );

			try { Directory.Delete( folder, true ); } catch ( IOException ) { }
		}
	}

	//The launcher's panel, out of the binary beside the engine.
	private static Panel Build( out RootPanel root, int width = 940, int height = 620 )
	{
		var game = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( game, $"{EngineEnvironment.Folder} is not set" );

		var dll = Path.Combine( game, "Rinten.dll" );

		Assert.IsTrue( File.Exists( dll ), $"the launcher is not built: {dll}" );

		var assembly = Assembly.LoadFrom( dll );
		var type = assembly.GetType( "Editor.LauncherPanel" );

		Assert.IsNotNull( type, "the launcher's panel is not in its binary" );

		var panel = (Panel)Activator.CreateInstance( type );

		EditorStyles.Apply( panel, "LauncherPanel.razor.scss" );

		root = new UiWindowRoot();
		root.AddChild( panel );

		//Advance the panel clock a second in 0.1s steps so build, layout and card animations complete
		for ( var i = 0; i < 12; i++ )
		{
			PanelRealTime.TimeDelta = 0.1;
			PanelRealTime.TimeNow += 0.1;

			root.TickInternal();
			root.PreLayout( new Rect( 0, 0, width, height ) );
			root.CalculateLayout();
			root.PostLayout();
		}

		return panel;
	}

	//Winds the clock on a window that is already built, so a change made to the panel is laid out and animated the way a running launcher would.
	private static void Tick( RootPanel root, Panel panel, int frames = 12, int width = 940, int height = 620 )
	{
		for ( var i = 0; i < frames; i++ )
		{
			PanelRealTime.TimeDelta = 0.1;
			PanelRealTime.TimeNow += 0.1;

			panel.StateHasChanged();

			root.TickInternal();
			root.PreLayout( new Rect( 0, 0, width, height ) );
			root.CalculateLayout();
			root.PostLayout();
		}
	}

	//Clicks through the projects tab to the new-project form, pressing rather than setting pages
	private static void OpenSetup( RootPanel root, Panel panel )
	{
		var add = panel.Descendants.FirstOrDefault( x => x.HasClass( "add" ) );

		Assert.IsNotNull( add, "the projects page has no card for making one" );

		add.CreateEvent( "onclick" );
		Tick( root, panel );

		//Not the first tile: that one is "a project already on this machine", which opens a folder dialog rather than the form.
		var tile = panel.Descendants.FirstOrDefault( x => x.HasClass( "type" ) && !x.HasClass( "existing" ) );

		Assert.IsNotNull( tile, "nothing on the tiles page says what a new project could be" );

		tile.CreateEvent( "onclick" );
		Tick( root, panel );
	}

	//A panel with that class that is not on its way out - a page left behind is deleted next frame.
	private static Panel Showing( Panel panel, string cls ) =>
		panel.Descendants.FirstOrDefault( x => x.HasClass( cls ) && !x.IsDeleting );

	//The tree, as somebody looking at a broken window would want it.
	private static string Describe( Panel panel, int depth = 0 )
	{
		var text = new StringBuilder();
		var rect = panel.Box.Rect;
		var words = panel is Rinten.UI.Label label && !string.IsNullOrEmpty( label.Text ) ? $" \"{label.Text}\"" : "";

		text.AppendLine( $"{new string( ' ', depth * 2 )}{panel.ElementName} [{panel.Classes}]{words} " +
			$"{rect.Left:F0},{rect.Top:F0} {rect.Width:F0}x{rect.Height:F0} " +
			$"{( panel.IsVisible ? "" : "hidden " )}op:{panel.ComputedStyle?.Opacity:F2}" );

		foreach ( var child in panel.Children ) text.Append( Describe( child, depth + 1 ) );

		return text.ToString();
	}

	//The rows down the side - the two tabs and the quieter rows under them - are there and have a size.
	[TestMethod]
	public void TheSideHasItsRows()
	{
		var panel = Build( out _ );

		var tabs = panel.Descendants.Where( x => x.HasClass( "tab" ) ).ToList();
		var links = panel.Descendants.Where( x => x.HasClass( "link" ) ).ToList();

		Assert.AreEqual( 2, tabs.Count, $"the side has {tabs.Count} tabs\n{Describe( panel )}" );
		Assert.IsTrue( links.Count >= 2, $"the side has {links.Count} rows under them\n{Describe( panel )}" );

		foreach ( var row in tabs.Concat( links ) )
		{
			Assert.IsTrue( row.Box.Rect.Width > 1 && row.Box.Rect.Height > 1,
				$"a row down the side has no size: {row.Box.Rect}\n{Describe( panel )}" );

			Assert.IsTrue( row.IsVisible, $"a row down the side is not visible\n{Describe( panel )}" );
		}
	}

	//The tabs switch the page: the engines, and the projects back again.
	[TestMethod]
	public void TheTabsChangeThePage()
	{
		var panel = Build( out var root );

		var tabs = panel.Descendants.Where( x => x.HasClass( "tab" ) ).ToList();

		Assert.AreEqual( 2, tabs.Count, $"the side has {tabs.Count} tabs\n{Describe( panel )}" );

		Assert.IsTrue( tabs[0].HasClass( "on" ), $"the launcher opens on neither tab\n{Describe( panel )}" );

		tabs[1].CreateEvent( "onclick" );
		Tick( root, panel );

		Assert.IsNotNull( Showing( panel, "releases" ),
			$"pressing the second tab did not bring up the engines\n{Describe( panel )}" );

		Assert.IsNull( Showing( panel, "list" ),
			$"the projects are still on the screen under the engines\n{Describe( panel )}" );

		//And back, which is the same press on the other one.
		panel.Descendants.First( x => x.HasClass( "tab" ) ).CreateEvent( "onclick" );
		Tick( root, panel );

		Assert.IsNotNull( Showing( panel, "list" ),
			$"pressing the first tab did not bring the projects back\n{Describe( panel )}" );
	}

	//A project in the list is a card in the window, with a size, visible, and not left transparent by an animation that never ran.
	[TestMethod]
	public void EveryProjectIsACard()
	{
		using var seeded = new AProjectInTheList();

		var panel = Build( out _ );

		var cards = panel.Descendants.Where( x => x.HasClass( "card" ) ).ToList();

		//One project was put in the list above, so there is one card at least - and what
		//matters is that a project the launcher counted is a card somebody can see.
		if ( cards.Count == 0 ) Assert.Fail( $"no cards in the list\n{Describe( panel )}" );

		foreach ( var card in cards )
		{
			Assert.IsTrue( card.Box.Rect.Height > 1,
				$"a project's card has no height: {card.Box.Rect}\n{Describe( panel )}" );

			Assert.IsTrue( card.IsVisible, $"a project's card is not visible\n{Describe( panel )}" );

			Assert.IsTrue( card.ComputedStyle?.Opacity > 0.01f,
				$"a project's card is invisible: opacity {card.ComputedStyle?.Opacity}\n{Describe( panel )}" );
		}
	}

	//Every label in the launcher has a drawn text texture, not just a size in the layout
	[TestMethod]
	public void EveryLabelHasSomethingToDraw()
	{
		var panel = Build( out var root );

		var missing = new StringBuilder();
		var labels = 0;
		var pictured = 0;

		foreach ( var label in panel.Descendants.OfType<Rinten.UI.Label>() )
		{
			if ( string.IsNullOrEmpty( label.Text ) ) continue;
			if ( !label.IsVisible ) continue;

			labels++;

			if ( label._textBlock?.FinishedTexture() is not null )
			{
				pictured++;
				continue;
			}

			missing.AppendLine( $"  \"{label.Text}\" [{label.Classes}] {label.Box.Rect} " +
				$"font {label.ComputedStyle?.FontFamily} {label.ComputedStyle?.FontSize}" );
		}

		Assert.AreEqual( labels, pictured,
			$"{labels - pictured} of {labels} labels have no picture to draw:\n{missing}" );
	}

	//Final colours match the sheet, i.e. hover transitions get stepped instead of staying black
	[TestMethod]
	public void TheColoursAreTheOnesTheSheetAsksFor()
	{
		var panel = Build( out var root );

		foreach ( var label in panel.Descendants.Where( x => x.HasClass( "label" ) ) )
		{
			var colour = label.ComputedStyle?.FontColor ?? Color.Black;

			Assert.IsTrue( colour.r > 0.2f && colour.a > 0.2f,
				$"a row down the side is painted {colour} - it fades on hover, and the fade never ran" );
		}

		//The buttons that make a project live on the third page of the tab.
		OpenSetup( root, panel );

		var primary = panel.Descendants.FirstOrDefault( x => x.HasClass( "primary" ) );
		var ghost = panel.Descendants.FirstOrDefault( x => x.HasClass( "ghost" ) );

		Assert.IsNotNull( primary );
		Assert.IsNotNull( ghost );

		var background = primary.ComputedStyle?.BackgroundColor ?? Color.Transparent;
		var plain = ghost.ComputedStyle?.BackgroundColor ?? Color.Transparent;

		//Which colour it is is the sheet's business - the design has moved it from blue to green once already. What is worth a test is that it has one
		//at all and that it is not the same surface every other button has: the one thing on the screen that does something is supposed to look like it.
		Assert.IsTrue( background.a > 0.5f,
			$"the button people press to make a project is painted {background}" );

		Assert.IsTrue( background != plain,
			"the button people press to make a project looks like every other button" );
	}

	//The icons are elements with a name in them, in the family the icons are in.
	[TestMethod]
	public void TheIconsAskForTheIconFont()
	{
		var panel = Build( out _ );

		var icons = panel.Descendants.Where( x => x.ElementName == "i" ).ToList();

		Assert.IsTrue( icons.Count > 0, $"the launcher has no icons at all\n{Describe( panel )}" );

		foreach ( var icon in icons )
		{
			Assert.AreEqual( "Material Icons", icon.ComputedStyle?.FontFamily,
				$"an icon is not asking for the icon font\n{Describe( panel )}" );

			Assert.IsTrue( icon.Box.Rect.Width > 1,
				$"an icon has no width: {icon.Box.Rect}\n{Describe( panel )}" );
		}
	}
}
