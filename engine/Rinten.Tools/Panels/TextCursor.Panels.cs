namespace Editor;

/// <summary>A place in a text edit's text.</summary>
public partial class TextCursor
{
	private TextEdit ownerManaged;
	private int blockManaged;
	private bool selectedBlock;

	internal bool Managed => ownerManaged is not null;

	internal TextCursor( TextEdit owner, int block )
	{
		ownerManaged = owner;
		blockManaged = block;
	}

	internal int BlockManaged => blockManaged;

	internal void SelectBlockManaged() => selectedBlock = true;

	internal void RemoveSelectionManaged()
	{
		if ( !selectedBlock ) return;

		selectedBlock = false;

		ownerManaged.RemoveLineManaged( blockManaged );
	}
}
