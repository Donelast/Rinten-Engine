using Editor;
using Rinten;

namespace EngineTests;

//The one bar along the top of a window: menus, window buttons and the room between them.
[TestClass]
[DoNotParallelize]
public class EditorTitleBarTests
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

	private static (Window Window, TitleBar Bar, Widget Canvas) Open( float width = 1000, float height = 600 )
	{
		var window = new Window();

		window.Canvas = new Widget( window );
		window.Size = new Vector2( width, height );

		Arrange( window );

		return (window, window.MenuWidget as TitleBar, window.Canvas);
	}

	[TestMethod]
	public void TheWindowSpendsOneRowOnItsBarAndTheRestIsTheCanvas()
	{
		var (window, bar, canvas) = Open();

		Assert.IsNotNull( bar, "a window opens with a bar of its own" );

		Assert.AreEqual( 0.0f, bar.Position.y, "the bar is the top of the window" );
		Assert.AreEqual( TitleBar.BarHeight, bar.Size.y, "one row, whatever is in it" );

		//The caption used to be a second strip above this one, and the canvas started under both of them.
		Assert.AreEqual( TitleBar.BarHeight, canvas.Position.y, "nothing between the bar and the canvas" );
		Assert.AreEqual( window.Size.y - TitleBar.BarHeight, canvas.Size.y );

		window.Destroy();
	}

	//A window laid out with a layout rather than a canvas starts under the bar too. The canvas was handed the room below it
	//and the layout was handed the whole window, so every dialog drew its first row over the window's own icon and buttons.
	[TestMethod]
	public void AWindowLaidOutRatherThanCanvassedAlsoStartsUnderTheBar()
	{
		var window = new Window();

		window.Layout = Layout.Column();
		window.Layout.Margin = 16;

		var first = new Widget( null );
		window.Layout.Add( first, 1 );

		window.Size = new Vector2( 800, 400 );

		Arrange( window );

		Assert.AreEqual( TitleBar.BarHeight + 16.0f, first.Position.y, 0.5f, "under the bar, and then the layout's own margin" );
		Assert.AreEqual( 16.0f, first.Position.x, 0.5f, "the bar takes nothing off the sides" );

		window.Destroy();
	}

	[TestMethod]
	public void MinimiseMaximiseAndCloseAreAtTheEndOfTheSameRow()
	{
		var (window, bar, _) = Open();

		var controls = bar.WindowControls;

		Assert.AreEqual( 3, controls.childWidgets.Count, "minimise, maximise, close" );

		Assert.AreEqual( bar.Size.x, controls.Position.x + controls.Size.x, 1.0f, "hard against the right edge" );
		Assert.AreEqual( TitleBar.BarHeight, controls.Size.y, "as tall as the bar" );

		foreach ( var control in controls.childWidgets )
			Assert.AreEqual( TitleBar.BarHeight, control.Size.y, "and so is every button in it" );

		window.Destroy();
	}

	[TestMethod]
	public void TheMenusShareTheRowWithTheButtons()
	{
		var (window, bar, _) = Open();

		bar.MenuBar.AddMenu( "File" );
		bar.MenuBar.AddMenu( "Edit" );

		Arrange( window );

		Assert.IsTrue( bar.MenuBar.Position.y >= 0.0f, "the menus are in the bar, not under it" );
		Assert.IsTrue( bar.MenuBar.Position.y + bar.MenuBar.Size.y <= TitleBar.BarHeight, "and inside it" );
		Assert.IsTrue( bar.MenuBar.Position.x < bar.WindowControls.Position.x, "and to the left of the buttons" );

		window.Destroy();
	}

	[TestMethod]
	public void WhatNobodyDrewOnIsHandedOverAsTheStripToDragBy()
	{
		var (window, bar, _) = Open();

		bar.MenuBar.AddMenu( "File" );

		Arrange( window );

		var areas = bar.DragAreas();

		Assert.AreNotEqual( 0, areas.Length, "a window with one menu has room left to be dragged by" );

		foreach ( var area in areas )
		{
			Assert.IsTrue( area.Width > 0, "an empty strip drags nothing" );
			Assert.AreEqual( bar.Size.y, area.Height, "the whole height of the bar" );

			//Anything the window system takes for a drag never reaches the panels, so a button inside one would be a button that cannot be pressed.
			Assert.IsTrue( area.Right <= bar.WindowControls.Position.x + 1.0f, "and never over the window buttons" );
		}

		window.Destroy();
	}

	[TestMethod]
	public void AWindowWithNoMenusStillGetsTheSameBar()
	{
		var (window, bar, canvas) = Open( 420, 300 );

		Assert.AreEqual( 0, bar.MenuBar.Menus.Count );

		Assert.AreEqual( TitleBar.BarHeight, bar.Size.y, "empty, but the same height" );
		Assert.AreEqual( TitleBar.BarHeight, canvas.Position.y );

		Assert.AreEqual( 3, bar.WindowControls.childWidgets.Count, "and it still closes" );

		window.Destroy();
	}
}
