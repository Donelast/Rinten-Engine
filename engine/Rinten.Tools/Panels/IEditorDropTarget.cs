namespace Editor;

/// <summary>Panel that accepts <see cref="EditorDrag"/> drops from editor widgets.</summary>
public interface IEditorDropTarget
{
	bool AcceptEditorDrops { get; }

	DropAction OnEditorDragHover( Widget.DragEvent ev );

	void OnEditorDragDrop( Widget.DragEvent ev );

	void OnEditorDragLeave();
}
