namespace Editor.MeshEditor;

partial class MeshTool
{
	//Material palette and move modes live on the scene view sidebar, not in this tool's footer

	public override Panel CreateShortcutsPanel() => new MeshToolShortcutsPanel();
}

//The mode keys - the digits, W to Y - are not here. They belong to the strip that draws the modes, which is on every scene view
//- see ToolSidebarPanel.Shortcuts . Declared here as well they answered a second time, and only while this tool was in hand.
file class MeshToolShortcutsPanel : EditorPanel
{
	[Shortcut( "tools.primitive-tool", "Shift+B", typeof( SceneViewPanel ) )]
	public void ActivatePrimitiveTool() => EditorToolManager.SetSubTool( nameof( PrimitiveTool ) );
}
