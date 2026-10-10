namespace Editor.MdlEditor;

//Marks a panel as a node type's full-page editor; needs a (TNode, Window) ctor and INodePage
[AttributeUsage( AttributeTargets.Class )]
internal sealed class NodePageAttribute( Type nodeType ) : Attribute
{
	public Type NodeType { get; } = nodeType;
}

//Implemented by node page panels so undo can swap the graph out from under them
//without recreating the panel - keeps view position and selection, no flicker.
internal interface INodePage
{
	//Point the page at its node in a rebuilt graph. Return false if it isn't the same node - the page gets recreated instead.
	bool Rebind( Node node );

	//The canvas whose view gets remembered when navigating away.
	CanvasPanel ViewCanvas => this as CanvasPanel;

	//The graph view currently receiving editing shortcuts.
	View FocusedGraphView => null;

	//A graph view hosted inside this page.
	View EmbeddedGraphView => null;

	bool CanCutSelection => false;
	bool CanCopySelection => false;
	bool CanPasteSelection => false;
	bool CutSelection() => false;
	bool CopySelection() => false;
	bool PasteSelection() => false;

	//Whether selections on this page can expose a secondary graph.
	bool SupportsSecondaryView => false;

	void OnGraphEdited() { }
}
