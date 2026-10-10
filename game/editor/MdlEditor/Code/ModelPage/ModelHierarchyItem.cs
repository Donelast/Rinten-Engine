namespace Editor.MdlEditor;

internal enum ModelHierarchyKind
{
	Bone,
}

//One row of the hierarchy - see ModelHierarchy.
internal sealed class ModelHierarchyItem
{
	public ModelHierarchyKind Kind { get; init; }
	public string Name { get; init; }
	public int Depth { get; init; }
	public string Group { get; init; }
}
