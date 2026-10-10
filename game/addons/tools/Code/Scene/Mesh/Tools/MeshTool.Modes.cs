namespace Editor.MeshEditor;

partial class MeshTool
{
	/// <summary>Mode column: four pick modes and shape tool in fixed order, then the remaining sub-tools</summary>
	public override ToolGroup CreateModeGroup()
	{
		var group = new ToolGroup( "Modes", ToolButtonSize.Mode );

		group.AddRange( MeshSelectionModes.Picks( Manager ) );
		group.Add( MeshSelectionModes.Shape( this ) );
		group.AddRange( SubToolCommand.ForSubToolsOf( this, MeshSelectionModes.Represents ) );

		return ToolGroup.OrNull( group );
	}

	/// <summary>What the mode that is on does to what it has picked: the move modes.</summary>
	public override ToolGroup CreateOptionGroup() => MeshMoveModes.Group( this );
}
