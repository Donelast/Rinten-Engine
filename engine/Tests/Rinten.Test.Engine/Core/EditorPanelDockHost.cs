using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class EditorPanelDockHostTests
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
	public void PanelDockHostFillsHolderAndParentsUnderRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 400, 300 );

		var holder = new PanelDockHost( null );
		holder.Size = new Vector2( 400, 300 );
		root.AddChild( holder.Surface );

		var child = new Panel();
		child.AddClass( "test-inspector" );
		holder.SetContent( child );

		holder.SettleFlexSubtree();
		root.SettleLayout();

		Assert.IsTrue( child.Parent == holder.Surface,
			"content panel should be parented to the host surface" );
		Assert.IsTrue( child.Box.Rect.Width > 350,
			"panel should receive most of the holder width" );
		Assert.IsTrue( child.Box.Rect.Height > 250,
			"panel should receive most of the holder height" );
	}
}
