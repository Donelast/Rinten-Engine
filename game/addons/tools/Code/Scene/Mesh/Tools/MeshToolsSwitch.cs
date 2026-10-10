namespace Editor.MeshEditor;

/// <summary>Whether the tool strips are over the scene view at all: the hammer in the header bar.</summary>
public static class MeshToolsSwitch
{
	private const string Cookie = "SceneView.ShowTools";

	//What the cookie said, once it has been asked.
	private static bool? shown;

	private const string ActionsCookie = "SceneView.ShowToolActions";

	private static bool? actionsShown;

	/// <summary>Whether the list of what a mode can run - merge, bevel, bridge - is shown beside the modes. The mapping tool's settings panel has the same commands, so somebody working there can put the list away.</summary>
	public static bool ActionsShown
	{
		get
		{
			if ( actionsShown is { } known ) return known;

			bool read;
			//Put away until asked for: the settings panel has the same commands
			try { read = EditorCookie.Get( ActionsCookie, false ); }
			catch ( Exception ) { read = false; }

			actionsShown = read;

			return read;
		}
		set
		{
			if ( ActionsShown == value ) return;

			actionsShown = value;

			try { EditorCookie.Set( ActionsCookie, value ); }
			catch ( Exception ) { }
		}
	}

	/// <summary>Whether the strips are shown.</summary>
	public static bool Shown
	{
		get
		{
			if ( shown is { } known ) return known;

			bool read;
			try { read = EditorCookie.Get( Cookie, true ); }
			catch ( Exception ) { read = true; }

			shown = read;

			return read;
		}
		set
		{
			if ( Shown == value ) return;

			shown = value;

			try { EditorCookie.Set( Cookie, value ); }
			catch ( Exception ) { }

			//Somebody left in face mode when its button goes is somebody with no way back to picking objects. Put
			//away the same way the mapping workspace puts it away on the way out - see MappingWorkspaceTools.OnLeft.
			if ( !value ) EditorToolManager.SetSubTool( nameof( ObjectSelection ) );
		}
	}
}
