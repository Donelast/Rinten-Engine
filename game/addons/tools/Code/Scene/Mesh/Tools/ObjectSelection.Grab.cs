namespace Editor.MeshEditor;

//Drag a selected object itself to move it, sliding along the grabbed face, snapped like the gizmo
//Only the viewport that received the press owns the drag; shares the gizmo's move code
partial class ObjectSelection
{
	//What the pointer is over this frame, found by UpdateHovered.
	SceneTraceResult _hovered;

	//The selected object the press landed on, for as long as the button is down.
	GameObject _grabFrom;

	//The viewport the press was in, which is the only one whose frames move it.
	SceneViewportPanel _grabView;

	//The plane it slides along.
	Plane _grabPlane;

	//Where on that plane it was taken.
	Vector3 _grabPoint;

	//The pivot when it was taken: the move is measured and snapped from here.
	Vector3 _grabPivot;

	//Whether it is being carried now.
	bool _grabbing;

	//Whether this press carried something, until the next one - so letting go at the end of a drag is not a click on nothing, which clears the selection.
	bool _grabbed;

	//The view this put a cursor on, so it is put back when the pointer leaves.
	SceneRenderingPanel _cursorOn;

	//How far the pointer goes before a press on an object is a drag: the distance GameObject.DrawGizmos
	//lets a press on an object go at. Before it a press is a click, and a click selects.
	const float GrabThreshold = 10.0f;

	void UpdateGrab()
	{
		var view = SceneViewportPanel.Framing;

		//Another viewport's frame: the drag is not its to move or to end - see EditorToolManager.IsInputView.
		if ( !IsInputView ) return;

		if ( Gizmo.WasLeftMousePressed && Gizmo.HasMouseFocus )
		{
			_grabbed = false;
			_grabFrom = null;
			_grabView = null;

			//Alt belongs to the camera.
			if ( Gizmo.IsAltPressed ) return;
			if ( SelectedUnder( _hovered ) is not { } taken ) return;
			if ( !PressIsOn( _hovered.GameObject ) ) return;

			_grabFrom = taken;
			_grabView = view;
			_grabPoint = _hovered.HitPosition;
			_grabPlane = new Plane( _grabPoint, GrabNormal( _hovered.Normal ) );
			_grabPivot = Pivot.Position;

			return;
		}

		if ( _grabFrom is null ) return;

		if ( !Gizmo.IsLeftMouseDown )
		{
			EndGrab();
			return;
		}

		if ( !_grabbing )
		{
			if ( Gizmo.CursorDragDelta.Length <= GrabThreshold ) return;

			if ( !_grabFrom.IsValid() )
			{
				_grabFrom = null;
				_grabView = null;
				return;
			}

			//The press is this drag's from here on. Taken off the object, so letting go is not a click that picks it
			//again - which with several selected would leave only this one - and so nothing else goes on holding it.
			Gizmo.Pressed.ClearPath();

			_grabbing = true;
			_grabbed = true;

			Pivot.BeginDrag();
			OnStartDrag();
		}

		if ( Application.IsKeyDown( KeyCode.Escape ) )
		{
			Translate( Vector3.Zero );
			OnUpdateDrag();

			EndGrab();
			return;
		}

		//A ray along the plane meets it nowhere; the object stays where it was last put rather than jumping.
		if ( !_grabPlane.TryTrace( Gizmo.CurrentRay, out var point, true ) ) return;

		var target = Gizmo.Snap( _grabPivot, point - _grabPoint, Rotation.Identity );

		//Translate carries an installed pivot along; one that isn't follows the selection.
		Translate( target - _grabPivot );
		OnUpdateDrag();
	}

	void EndGrab()
	{
		if ( _grabbing )
		{
			OnEndDrag();

			//After OnEndDrag, so the move's undo entry exists for the pivot to fold into.
			Pivot.EndDrag();

			//The gizmo stands where the pivot went.
			Tool?.MoveMode?.OnBegin( this );
		}

		_grabbing = false;
		_grabFrom = null;
		_grabView = null;
	}

	//Whether the press landed on the object (or its own scope), not on a gizmo handle
	static bool PressIsOn( GameObject go )
	{
		if ( !Gizmo.Pressed.Any ) return true;
		if ( !go.IsValid() ) return false;

		using ( Gizmo.ObjectScope( go, go.WorldTransform ) )
		{
			return Gizmo.Pressed.ThisOrChild;
		}
	}

	//The plane a drag slides along: the view's own in a 2D view, the face that
	//was taken hold of in a 3D one - squared to the nearest world axis either way.
	static Vector3 GrabNormal( Vector3 faceNormal )
	{
		if ( Gizmo.Camera is { Ortho: true } camera )
			return AxisOf( camera.Rotation.Forward );

		return AxisOf( faceNormal );
	}

	//A moving cursor over something that can be taken and while it is carried, in the view the pointer is in. On the view's own picture, which is the panel
	//under the pointer: the window takes its cursor from that panel's style. A widget's cursor is a style on the widget's root panel and never reached it.
	void UpdateCursor()
	{
		var framing = SceneViewportPanel.Framing;
		var renderer = framing?.Renderer;
		if ( renderer is null ) return;

		var carrying = _grabbing && framing == _grabView;

		if ( !Gizmo.HasMouseFocus && !carrying )
		{
			if ( renderer == _cursorOn ) SetGrabCursor( null, null );
			return;
		}

		var wanted = carrying ? "grabbing"
			: !Gizmo.IsLeftMouseDown && !IsBoxSelecting && SelectedUnder( _hovered ) is not null ? "grab"
			: null;

		SetGrabCursor( renderer, wanted );
	}

	void ResetGrabCursor() => SetGrabCursor( null, null );

	void SetGrabCursor( SceneRenderingPanel renderer, string cursor )
	{
		//Off the view it was on, when it moves to another one or goes.
		if ( _cursorOn is not null && _cursorOn != renderer )
		{
			if ( _cursorOn.IsValid() ) _cursorOn.Style.Cursor = null;
			_cursorOn = null;
		}

		if ( renderer is null || !renderer.IsValid() ) return;

		if ( renderer.Style.Cursor != cursor ) renderer.Style.Cursor = cursor;

		_cursorOn = cursor is null ? null : renderer;
	}

	//The selected object a hit belongs to, itself or the nearest selected one above it.
	GameObject SelectedUnder( SceneTraceResult hit )
	{
		if ( !hit.Hit ) return null;

		for ( var go = hit.GameObject; go.IsValid() && go is not Rinten.Scene; go = go.Parent )
		{
			if ( Array.IndexOf( _objects, go ) >= 0 ) return go;
		}

		return null;
	}

	//The world axis nearest a direction.
	static Vector3 AxisOf( Vector3 direction )
	{
		var a = direction.Abs();

		if ( a.x <= 0.0f && a.y <= 0.0f && a.z <= 0.0f ) return Vector3.Up;

		if ( a.x >= a.y && a.x >= a.z ) return new Vector3( 1, 0, 0 );
		if ( a.y >= a.z ) return new Vector3( 0, 1, 0 );

		return new Vector3( 0, 0, 1 );
	}
}
