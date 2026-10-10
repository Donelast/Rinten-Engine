namespace Editor.MeshEditor;

//Per-mode command buttons beside the move modes; run via their shortcut, dim until enough picked
internal static class MeshActions
{
	internal readonly record struct Entry( string Section, string Title, string Icon, string Shortcut, int Needs = 1 );

	internal static ToolGroup Build<T>( EditorTool tool, string name, params Entry[] actions )
	{
		var group = new ToolGroup( name, ToolButtonSize.Action );

		foreach ( var action in actions )
		{
			var needs = action.Needs;

			group.Add( new ToolCommand( action.Icon, action.Title, () => EditorTool.InvokeShortcut( action.Shortcut ) )
			{
				Section = action.Section,
				Shortcut = action.Shortcut,
				Enabled = () => needs == 0 || tool.Selection.OfType<T>().Count() >= needs,
			} );
		}

		return ToolGroup.OrNull( group );
	}
}

partial class VertexTool
{
	public override ToolGroup CreateActionGroup() => MeshActions.Build<MeshVertex>( this, "Vertex",
		new( "Modify", "Merge", "merge", "mesh.merge", 2 ),
		new( "Modify", "Connect", "timeline", "mesh.connect", 2 ),
		new( "Modify", "Bevel", "rounded_corner", "mesh.bevel" ),
		new( "Modify", "Snap To Vertex", "adjust", "mesh.snap_to_vertex", 2 ),
		new( "Tools", "Edge Cut", "content_cut", "mesh.edge-cut-tool", 0 ) );
}

partial class EdgeTool
{
	public override ToolGroup CreateActionGroup() => MeshActions.Build<MeshEdge>( this, "Edge",
		new( "Modify", "Dissolve", "remove_circle_outline", "mesh.dissolve" ),
		new( "Modify", "Collapse", "compress", "mesh.collapse" ),
		new( "Modify", "Connect", "timeline", "mesh.connect", 2 ),
		new( "Modify", "Extend", "open_in_full", "mesh.extend" ),
		new( "Construct", "Merge", "merge", "mesh.merge", 2 ),
		new( "Construct", "Split", "call_split", "mesh.split" ),
		new( "Construct", "Fill Hole", "format_color_fill", "mesh.fill-hole" ),
		new( "Construct", "Bridge", "linear_scale", "mesh.bridge-edges", 2 ),
		new( "Tools", "Bevel", "rounded_corner", "mesh.edge-bevel" ),
		new( "Tools", "Quick Bevel", "bolt", "mesh.edge-quick-bevel" ),
		new( "Tools", "Edge Cut", "content_cut", "mesh.edge-cut-tool", 0 ),
		new( "Tools", "Edge Arch", "looks", "mesh.edge-arch-tool" ),
		new( "Tools", "Extrude Along Path", "vertical_align_bottom", "mesh.path-extrude" ),
		new( "Select", "Loop", "loop", "mesh.select-loop" ),
		new( "Select", "Ring", "radio_button_unchecked", "mesh.select-ring" ) );
}

partial class FaceTool
{
	public override ToolGroup CreateActionGroup() => MeshActions.Build<MeshFace>( this, "Face",
		new( "Modify", "Extract", "unarchive", "mesh.extract-faces" ),
		new( "Modify", "Detach", "link_off", "mesh.detach-faces" ),
		new( "Modify", "Combine", "merge_type", "mesh.combine-faces" ),
		new( "Modify", "Thicken", "layers", "mesh.thicken-faces" ),
		new( "Modify", "Flip", "flip", "mesh.flip-all-faces" ),
		new( "Tools", "Inset", "crop_free", "mesh.inset-tool" ),
		new( "Tools", "Bridge", "linear_scale", "mesh.bridge-tool", 2 ),
		new( "Tools", "Edge Cut", "content_cut", "mesh.edge-cut-tool", 0 ),
		new( "Tools", "Slice", "horizontal_split", "mesh.quad-slice" ),
		new( "Tools", "Mirror", "compare", "mesh.mirror-tool" ),
		new( "Tools", "Clip", "content_cut", "mesh.open-clipping-tool" ),
		new( "Texture", "Fast Texture", "wallpaper", "mesh.fast-texture-tool", 0 ),
		new( "Texture", "Find / Replace", "find_replace", "mesh.find-replace-material-tool", 0 ) );
}

partial class ObjectSelection
{
	public override ToolGroup CreateActionGroup() => MeshActions.Build<GameObject>( this, "Object",
		new( "Origin", "Set Origin To Pivot", "gps_fixed", "mesh.set-origin-to-pivot" ),
		new( "Origin", "Center Origin", "filter_center_focus", "mesh.center-origin" ),
		new( "Meshes", "Merge", "merge", "mesh.merge-meshes", 2 ),
		new( "Meshes", "Separate", "call_split", "mesh.separate-components" ),
		new( "Meshes", "Flip Faces", "flip", "mesh.flip-all-mesh-faces" ),
		new( "Meshes", "Convert To Mesh", "category", "mesh.convert-model-to-mesh" ),
		new( "Tools", "Clip", "content_cut", "mesh.open-clipping-tool" ),
		new( "Tools", "Mirror", "compare", "mesh.mirror-tool" ),
		new( "Tools", "Boolean", "difference", "mesh.boolean-tool", 2 ) );
}
