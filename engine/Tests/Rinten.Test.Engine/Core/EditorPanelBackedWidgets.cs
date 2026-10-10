using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class EditorPanelBackedWidgetsTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;
	}

	[TestMethod]
	public void ControlWidgetFieldChromeDisablesCanvasDraw()
	{
		var property = new ReflectionHolder().GetSerialized().GetProperty( nameof( ReflectionHolder.Name ) );
		var field = new StringControlWidget( property );
		field.Size = new Vector2( 200, Theme.RowHeight );

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 400, 200 );
		root.AddChild( field.Surface );

		field.SyncSurfaceContent();

		Assert.IsFalse( field.UsesCanvasDraw, "field chrome panel should prevent bake" );
	}

	[TestMethod]
	public void BoolControlWidgetUsesPanelChrome()
	{
		var property = new ReflectionHolder().GetSerialized().GetProperty( nameof( ReflectionHolder.Flag ) );
		var box = new BoolControlWidget( property );
		box.Size = new Vector2( Theme.RowHeight, Theme.RowHeight );

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 100, 100 );
		root.AddChild( box.Surface );

		box.SyncSurfaceContent();

		Assert.IsFalse( box.UsesCanvasDraw, "bool chrome panel should prevent bake" );
	}

	[TestMethod]
	public void ContainerControlWidgetSkipsFieldChrome()
	{
		var property = new ReflectionHolder().GetSerialized().GetProperty( nameof( ReflectionHolder.Position ) );
		var vector = new VectorControlWidget( property );
		vector.Size = new Vector2( 300, Theme.RowHeight );

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 400, 200 );
		root.AddChild( vector.Surface );

		vector.SyncSurfaceContent();

		Assert.IsFalse( vector.UsesCanvasDraw, "container controls should not bake an empty field plate" );
	}

	private sealed class ReflectionHolder
	{
		public string Name { get; set; } = "";
		public Vector3 Position { get; set; }
		public bool Flag { get; set; }
	}
}
