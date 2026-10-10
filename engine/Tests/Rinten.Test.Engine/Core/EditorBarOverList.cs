using Editor;
using Rinten;

namespace EngineTests;

//A toolbar over a growing tree (browser panel shape): the bar must not take the tree's room
[TestClass]
[DoNotParallelize]
public class EditorBarOverListTests
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
	public void TheBarKeepsItsOwnHeightAndTheListGetsTheRest()
	{
		var panel = new Widget( null );

		panel.Size = new Vector2( 300, 400 );
		panel.Layout = Layout.Column();
		panel.Layout.Spacing = 4;
		panel.Layout.Margin = 4;

		var bar = panel.Layout.Add( new Widget( panel ) );
		bar.Layout = Layout.Row();
		bar.Layout.Spacing = 4;

		var search = bar.Layout.Add( new LineEdit( bar ) );

		//As the browser's own bar does: a spacer that pushes what follows to the right end of the row.
		bar.Layout.AddStretchCell();

		var find = bar.Layout.Add( new IconButton( "search" ) );
		var clear = bar.Layout.Add( new IconButton( "close" ) );

		//What the browser's tree says for itself.
		var list = panel.Layout.Add( new ListView( panel ) );
		list.SetSizeMode( SizeMode.CanGrow, SizeMode.CanGrow );

		Arrange( panel );

		System.Console.WriteLine( $"panel  {panel.Position} {panel.Size}" );

		System.Console.WriteLine( $"bar    {bar.Position} {bar.Size}" );
		System.Console.WriteLine( $"search {search.Position} {search.Size}" );
		System.Console.WriteLine( $"find   {find.Position} {find.Size}" );
		System.Console.WriteLine( $"list   {list.Position} {list.Size}" );

		//A row of controls is as tall as a control, whatever room there is.
		Assert.IsTrue( bar.Size.y <= Theme.RowHeight + 12.0f,
			$"the bar is {bar.Size.y} tall - it has taken the room the list needed" );

		//And the list has the rest of the panel.
		Assert.IsTrue( list.Size.y > 300.0f, $"the list is {list.Size.y} tall" );

		Assert.IsTrue( list.Position.y >= bar.Position.y + bar.Size.y - 0.5f, "the list is under the bar" );

		//The search box is in the bar, at the top of the panel, and not stretched to the height of it.
		Assert.IsTrue( search.Size.y <= Theme.RowHeight + 8.0f, $"the search box is {search.Size.y} tall" );
		Assert.IsTrue( search.Position.y < 40.0f, $"the search box is at y {search.Position.y}" );
	}
}
