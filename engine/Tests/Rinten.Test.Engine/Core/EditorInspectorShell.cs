using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class EditorInspectorShellTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;
		if ( Theme.RowHeight <= 0.0f ) Theme.RowHeight = 28.0f;
	}

	[TestMethod]
	public void PanelWidgetFillsHolderWithSingleChildPanel()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 320, 400 );

		var holder = new TestPanelHost( null );
		holder.Size = new Vector2( 320, 400 );
		root.AddChild( holder.Surface );

		var child = new Panel();
		child.AddClass( "inspector-content" );
		holder.SetChild( child );

		holder.SettleFlexSubtree();
		root.SettleLayout();

		Assert.IsNotNull( holder.Panel, "dock widget should host one root panel" );
		Assert.IsTrue( child.Parent == holder.Surface, "content should live on the widget surface" );
		Assert.IsTrue( child.Box.Rect.Width > 300, "panel should receive most of the holder width" );
		Assert.IsTrue( child.Box.Rect.Height > 350, "panel should receive most of the holder height" );
	}

	sealed class TestPanelHost : PanelWidget
	{
		public TestPanelHost( Widget parent ) : base( parent )
		{
			HorizontalSizeMode = SizeMode.Flexible;
			VerticalSizeMode = SizeMode.Flexible;
		}

		public void SetChild( Panel panel ) => SetPanel( panel );
	}
}
