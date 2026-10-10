namespace Editor.MeshEditor;

/// <summary>Selection modes (objects, faces, edges, vertices) plus the shape tool, shared by two tools</summary>
public static class MeshSelectionModes
{
	/// <summary>The tool every one of these modes is a mode of.</summary>
	public const string Mode = nameof( MeshTool );

	/// <summary>Picking whole objects, the one mode that is not mesh editing.</summary>
	public const string ObjectShortcut = "tools.object-selection";

	public const string FaceShortcut = "tools.face-tool";
	public const string EdgeShortcut = "tools.edge-tool";
	public const string VertexShortcut = "tools.vertex-tool";

	/// <summary>The shape tool's key, which is the one button that wears two glyphs.</summary>
	public const string ShapeShortcut = "tools.shape-tool";

	/// <summary>The four a click picks with, in the order they go down the column.</summary>
	public static IEnumerable<IToolCommand> Picks( EditorToolManager manager )
	{
		yield return Pick( manager, "Select Objects", "Whole objects, moved by a gizmo.",
			"highlight_alt", ObjectShortcut, nameof( ObjectSelection ) );

		yield return Pick( manager, "Select Faces", "The flats between those lines.",
			"crop_square", FaceShortcut, nameof( FaceTool ) );

		yield return Pick( manager, "Select Edges", "The lines between those corners.",
			"timeline", EdgeShortcut, nameof( EdgeTool ) );

		yield return Pick( manager, "Select Vertices", "The corners a mesh is made of.",
			"scatter_plot", VertexShortcut, nameof( VertexTool ) );
	}

	/// <summary>The shape button for a tool, or null where the tool has nothing that draws shapes.</summary>
	public static IToolCommand Shape( EditorTool owner )
		=> Drawing( owner ) is null ? null : new ShapeToolCommand( owner );

	/// <summary>Whichever of a tool's own sub-tools draws shapes, or null.</summary>
	public static PrimitiveTool Drawing( EditorTool owner )
		=> owner?.Tools?.OfType<PrimitiveTool>().FirstOrDefault();

	/// <summary>Whether the column already stands for this sub-tool: one of the four picks, or the shape tool, which have buttons of their own above the rest.</summary>
	public static bool Represents( EditorTool subTool )
		=> subTool is VertexTool or EdgeTool or FaceTool or ObjectSelection or PrimitiveTool;

	//A stale sub-tool name from the mode being left is harmless - the manager falls back to whatever that tool already had.
	/// <summary>Goes into a mode, which is what both the button and its key do.</summary>
	public static void Activate( string subTool )
	{
		EditorToolManager.CurrentModeName = Mode;
		EditorToolManager.SetSubTool( subTool );
	}

	//One of the four. Picking rather than toggling: pressing the one that is already lit does nothing, because there is no mode to fall back to.
	private static IToolCommand Pick( EditorToolManager manager, string title, string description,
		string icon, string shortcut, string subTool )
	{
		return new ToolCommand( icon, title, () => Activate( subTool ) )
		{
			Description = description,
			Shortcut = shortcut,
			Active = () => IsCurrent( manager, subTool ),
		};
	}

	private static bool IsCurrent( EditorToolManager manager, string subTool )
	{
		if ( EditorToolManager.CurrentModeName != Mode ) return false;

		//The sub-tool is switched a frame late, so this reads the live one rather than the name that was asked for.
		var current = manager?.CurrentSubTool;

		//Drawing a shape is the fifth of these and not a thing running beside them, so while it has the view none of the four is what a click does.
		if ( current is PrimitiveTool ) return false;

		return current?.GetType().Name == subTool;
	}
}

/// <summary>Picks the mode's own <see cref="PrimitiveTool"/>: the fifth of the modes.</summary>
public sealed class ShapeToolCommand : IToolCommand
{
	private readonly EditorTool owner;

	public ShapeToolCommand( EditorTool owner )
	{
		this.owner = owner;
	}

	public string Title => "Create Shape";

	public string Description => "Drag one out in the view, the same as the mapping tools do. Which shape is the strip beside this.";

	//A cube, and a plus over its corner: one glyph says what is drawn and the other says that this is what draws one.
	public string Icon => "view_in_ar";

	public string Badge => "add";

	public string Shortcut => MeshSelectionModes.ShapeShortcut;

	public string ToolTip => ToolCommandText.ToolTip( this );

	public bool IsActive => owner?.CurrentTool is PrimitiveTool;

	public bool IsEnabled => true;

	public void Execute()
	{
		if ( MeshSelectionModes.Drawing( owner ) is not { } drawing ) return;
		if ( owner.CurrentTool == drawing ) return;

		owner.CurrentTool = drawing;
	}
}
