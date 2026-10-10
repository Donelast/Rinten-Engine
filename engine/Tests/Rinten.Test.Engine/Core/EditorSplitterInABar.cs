using Editor;
using Rinten;

namespace EngineTests;

//A splitter inside a bar, which is how the asset browser is put together.
[TestClass]
[DoNotParallelize]
public class EditorSplitterInABarTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	private static void Arrange( Widget widget )
	{
		widget.ArrangeIfNeeded();

		foreach ( var child in widget.childWidgets ) Arrange( child );
	}

	[TestMethod]
	public void TheSearchBoxIsBesideThePathAndNotUnderIt()
	{
		var browser = new Widget( null );

		browser.Size = new Vector2( 600, 400 );
		browser.Layout = Layout.Column();

		//Made with the browser as their parent, as the browser's own are.
		var path = new Widget( browser );
		var search = new Widget( browser );

		path.MinimumHeight = 24;
		search.MinimumHeight = 24;

		var toolbar = browser.Layout.AddRow();

		var splitter = new Splitter( browser );
		splitter.IsHorizontal = true;
		splitter.AddWidget( path );
		splitter.AddWidget( search );
		splitter.SetStretch( 0, 9 );
		splitter.SetStretch( 1, 1 );

		toolbar.Add( splitter, 1 );

		var body = browser.Layout.Add( new Widget( browser ), 1 );

		Arrange( browser );

		System.Console.WriteLine( $"splitter {splitter.Position} {splitter.Size}" );
		System.Console.WriteLine( $"path     {path.Position} {path.Size}" );
		System.Console.WriteLine( $"search   {search.Position} {search.Size}" );
		System.Console.WriteLine( $"body     {body.Position} {body.Size}" );

		//The bar is at the top of the browser, and the body under it - which it only is if the splitter asked for a height of
		//its own. A splitter that asks for nothing gets a row of no height, and everything below is drawn over the top of it.
		Assert.AreEqual( 0.0f, splitter.Position.y, 0.5f, "the toolbar is the first row" );

		Assert.IsTrue( splitter.Size.y >= 24.0f,
			$"the splitter in the bar is {splitter.Size.y} tall, so the bar has no height" );

		Assert.IsTrue( body.Position.y >= splitter.Size.y - 0.5f,
			$"the body starts at {body.Position.y}, over a bar {splitter.Size.y} tall" );

		//Side by side inside the splitter, in the order they were added.
		Assert.AreEqual( 0.0f, path.Position.y, 0.5f, "the path is at the top of the splitter" );
		Assert.AreEqual( 0.0f, search.Position.y, 0.5f, "and so is the search box" );

		Assert.IsTrue( search.Position.x > path.Position.x,
			$"the search box is beside the path, not under it: path {path.Position}, search {search.Position}" );

		//Nine to one, as it was asked for.
		Assert.IsTrue( path.Size.x > search.Size.x, "the path takes the bigger share" );
	}

	//And the tree beside the list, which is the other splitter - the one whose left side is the folders.
	[TestMethod]
	public void TheFolderTreeIsBesideTheListAndHasRoomToBeSeen()
	{
		var browser = new Widget( null );

		browser.Size = new Vector2( 600, 400 );
		browser.Layout = Layout.Row();

		var tree = new Widget( browser );
		var list = new Widget( browser );

		var splitter = new Splitter( browser );
		splitter.IsHorizontal = true;
		splitter.AddWidget( tree );
		splitter.SetStretch( 0, 1 );
		splitter.AddWidget( list );
		splitter.SetStretch( 1, 5 );

		browser.Layout.Add( splitter );

		Arrange( browser );

		System.Console.WriteLine( $"tree {tree.Position} {tree.Size}" );
		System.Console.WriteLine( $"list {list.Position} {list.Size}" );

		Assert.IsTrue( tree.Size.x > 20.0f, $"the folder tree is {tree.Size.x} wide - nobody can see a folder in that" );
		Assert.IsTrue( tree.Size.y > 100.0f, $"the folder tree is {tree.Size.y} tall" );

		Assert.IsTrue( list.Position.x > tree.Position.x, "the list is beside the tree" );
	}
}
