using Editor;
using Rinten.UI;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class EditorInspectorPanelTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;
	}

	[TestMethod]
	public void InspectorPanelIsPanelWithFlexGrow()
	{
		var panel = new TestInspectorPanel();
		Assert.IsTrue( panel is Panel );
		Assert.AreEqual( 1.0f, panel.Style.FlexGrow );
	}

	sealed class TestInspectorPanel : InspectorPanel
	{
		public TestInspectorPanel() { }
	}
}
