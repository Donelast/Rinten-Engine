namespace Editor.SpriteEditor;

//What dragged frames carry: which frames, and which of them the pointer picked up - the one a drop is measured against.
internal sealed record FrameDragData( IReadOnlyList<int> Indices, int From );
