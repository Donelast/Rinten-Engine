namespace Editor;

//Mapping workspace scene behaviour: opens views split four ways and remembers the tool mode
internal static class MappingWorkspaceTools
{
	/// <summary>Whether mapping is the workspace being shown.</summary>
	public static bool Active { get; private set; }

	//The mode mapping was left in, so coming back puts you where you were. The first time in that is the mesh tools, which are what the workspace is for.
	private static string LastMode = nameof( MeshEditor.MeshTool );

	//Views waiting for relayout; deferred to next frame, the request comes from a dying viewport
	private static readonly List<SceneViewPanel> Pending = new();
	private static bool WaitAFrame;

	[Event( "mapping.entered" )]
	public static void OnEntered()
	{
		Active = true;

		EditorToolManager.CurrentModeName = LastMode;

		//Next frame, not this one: Open in Mapping is on the viewport's own context menu,
		//and splitting now would destroy the widget that click is still unwinding through.
		WaitAFrame = true;
		QueueAll();
	}

	[Event( "mapping.left" )]
	public static void OnLeft()
	{
		Active = false;
		LastMode = EditorToolManager.CurrentModeName;

		//The mode picker is only in here, so leaving in terrain or clutter mode would be leaving with no way back to moving objects around.
		EditorToolManager.CurrentModeName = nameof( MeshEditor.MeshTool );
		EditorToolManager.SetSubTool( nameof( MeshEditor.ObjectSelection ) );

		WaitAFrame = true;
		QueueAll();
	}

	/// <summary>A scene opened while mapping is the workspace comes up split like the rest.</summary>
	public static void ApplyTo( SceneViewPanel view ) => Queue( view );

	private static void QueueAll()
	{
		foreach ( var view in SceneViewPanel.All )
		{
			Queue( view );
		}
	}

	private static void Queue( SceneViewPanel view )
	{
		if ( !view.IsValid() ) return;
		if ( Pending.Contains( view ) ) return;

		Pending.Add( view );
	}

	[EditorEvent.Frame]
	public static void Frame()
	{
		if ( WaitAFrame )
		{
			WaitAFrame = false;
			return;
		}

		if ( Pending.Count == 0 ) return;

		var views = Pending.ToArray();
		Pending.Clear();

		foreach ( var view in views )
		{
			if ( !view.IsValid() ) continue;

			if ( Active )
				Split( view );
			else
				view.SetWorkspaceLayout( ViewportLayoutMode.One );
		}
	}

	//One 3D view with the three flat ones beside it, which is how you build something rather than how you look at it.
	private static void Split( SceneViewPanel view )
	{
		if ( !view.IsValid() ) return;

		view.SetWorkspaceLayout( ViewportLayoutMode.Four );

		SetViewMode( view, 0, ViewportViewMode.Perspective );
		SetViewMode( view, 1, ViewportViewMode.Top2d );
		SetViewMode( view, 2, ViewportViewMode.Front2d );
		SetViewMode( view, 3, ViewportViewMode.Side2d );
	}

	private static void SetViewMode( SceneViewPanel view, int id, ViewportViewMode mode )
	{
		if ( view.TryGetViewport( id, out var viewport ) )
			viewport.State.View = mode;
	}
}
