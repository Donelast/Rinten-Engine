using Rinten.UI;

namespace Editor.MdlEditor;

public partial class Preview
{
	//What the viewport is offering a handle for, if anything.
	internal ScenePlacement Placement { get; private set; }

	//Which handle it is offering.
	internal PlacementTool PlacementTool { get; private set; } = PlacementTool.Move;

	internal bool PlacementDragging { get; private set; }

	//A handle is up, so a click on one is meant for it - even on a model that is ragdolling, where there is a limb behind it to grab.
	protected override bool HandleBeatsGrab
		=> Placement is not null && PlacementTool != PlacementTool.None;

	//World axes when true, mesh axes when false; stored in the gizmo settings the handles read
	internal bool PlacementGlobalSpace => PreviewGizmoSettings.Current.GlobalSpace;

	//Told when the tool changes, so a toolbar can follow it.
	internal Action PlacementToolChanged { get; set; }

	//Drag start state, captured once; re-reading it mid-drag would compound the rotation
	private Vector3 _dragFrom;
	private Rotation _turnFrom = Rotation.Identity;
	private Rotation _dragBasis = Rotation.Identity;

	private Vector3 _movedBy;

	//Put a handle on this, or clear it with null.
	internal void SetPlacement( ScenePlacement placement )
	{
		Placement = placement is { IsValid: true } ? placement : null;
	}

	internal void SetPlacementTool( PlacementTool tool )
	{
		if ( PlacementTool == tool ) return;

		PlacementTool = tool;
		PlacementToolChanged?.Invoke();
	}

	//W, E, R, X, G and the brackets over the picture, the way every other viewport in the editor reads them.
	//Only on the way down: a key that acts twice would put the tool it just chose straight back off again.
	protected override void ViewButtonTyped( ButtonEvent e )
	{
		if ( !e.Pressed || e.HasCtrl || e.HasAlt )
			return;

		//The same keys the scene viewport answers to - see ViewportToolsPanel.Shortcuts. These are about the grid rather
		//than about what is selected, so unlike the tool keys below they work whether or not anything has a handle on it.
		switch ( e.Button )
		{
			case "x":
				SetPlacementGlobalSpace( !PlacementGlobalSpace );
				e.StopPropagation = true;
				return;

			case "g":
				SetGridSnap( !PreviewGizmoSettings.Current.SnapToGrid );
				e.StopPropagation = true;
				return;

			case "lbracket":
				StepGrid( -1 );
				e.StopPropagation = true;
				return;

			case "rbracket":
				StepGrid( 1 );
				e.StopPropagation = true;
				return;
		}

		if ( Placement is null )
			return;

		var tool = e.Button switch
		{
			"w" => PlacementTool.Move,
			"e" => PlacementTool.Rotate,
			"r" => PlacementTool.Scale,
			_ => PlacementTool.None
		};

		if ( tool == PlacementTool.None )
			return;

		SetPlacementTool( tool == PlacementTool ? PlacementTool.None : tool );
		e.StopPropagation = true;
	}

	internal void SetPlacementGlobalSpace( bool global )
	{
		if ( PlacementGlobalSpace == global ) return;

		PreviewGizmoSettings.Current.GlobalSpace = global;
		PreviewGizmoSettings.Save();
		PlacementToolChanged?.Invoke();
	}

	//Turn grid snapping on or off, and keep it.
	internal void SetGridSnap( bool snap )
	{
		if ( PreviewGizmoSettings.Current.SnapToGrid == snap ) return;

		PreviewGizmoSettings.Current.SnapToGrid = snap;
		PreviewGizmoSettings.Save();
		PlacementToolChanged?.Invoke();
	}

	//Turn angle snapping on or off, and keep it.
	internal void SetAngleSnap( bool snap )
	{
		if ( PreviewGizmoSettings.Current.SnapToAngles == snap ) return;

		PreviewGizmoSettings.Current.SnapToAngles = snap;
		PreviewGizmoSettings.Save();
		PlacementToolChanged?.Invoke();
	}

	//Halve the grid step, or double it.
	internal void StepGrid( int direction )
	{
		var settings = PreviewGizmoSettings.Current;
		var next = PreviewGizmoSettings.Step( settings.GridSpacing, direction );

		if ( next.AlmostEqual( settings.GridSpacing ) ) return;

		settings.GridSpacing = next;
		PreviewGizmoSettings.Save();
		PlacementToolChanged?.Invoke();
	}

	//Move, rotate, and scale handles. Hull radius still uses the overlay gizmos.
	private void DrawPlacement()
	{
		if ( Placement is null ) return;
		if ( PlacementTool == PlacementTool.None ) return;
		if ( PlacementTool == PlacementTool.Scale && !Placement.CanScale ) return;
		if ( PlacementTool == PlacementTool.Rotate && !Placement.CanTurn ) return;

		var position = Placement.GetPosition();
		var rotation = Placement.GetRotation();

		if ( position.IsNaN ) return;

		//Re-anchor only while the left button is up; Gizmo.Pressed can flicker mid-drag and spin the model
		if ( !Application.MouseButtons.HasFlag( MouseButtons.Left ) )
		{
			_dragFrom = position;
			_turnFrom = rotation.ToRotation();
			_dragBasis = PlacementGlobalSpace ? Rotation.Identity : _turnFrom;
			_movedBy = default;
		}

		var basis = _dragBasis;

		if ( PlacementTool == PlacementTool.Move )
		{
			//Drawn at the current position so the handle follows what it moves; mouse delta is plane-based
			using ( Gizmo.Scope( $"placement.{Placement.Name}", new Transform( position ) ) )
			{
				Gizmo.Hitbox.DepthBias = 0.01f;
				if ( !Gizmo.Control.Position( "position", Vector3.Zero, out var delta, basis ) ) return;

				_movedBy += delta;
				PlacementDragging = true;
				Placement.Set( Gizmo.Snap( _dragFrom, _movedBy, basis ), rotation );
			}

			return;
		}

		if ( PlacementTool == PlacementTool.Rotate )
		{
			using ( Gizmo.Scope( $"placement.{Placement.Name}", new Transform( position, basis ) ) )
			{
				Gizmo.Hitbox.DepthBias = 0.01f;
				if ( !Gizmo.Control.Rotate( "rotation", Rotation.Identity, out var turn ) ) return;

				//turn is the whole angle since the press, not this frame's share of it.
				PlacementDragging = true;
				Placement.Set( position, ( basis * Gizmo.Snap( turn ) * basis.Inverse * _turnFrom ).Angles() );
			}

			return;
		}

		using ( Gizmo.Scope( $"placement.{Placement.Name}", new Transform( position ) ) )
		{
			Gizmo.Hitbox.DepthBias = 0.01f;
			DrawPlacementScale( basis );
		}
	}

	private void DrawPlacementScale( Rotation basis )
	{
		var scale = Placement.GetScale();

		if ( Placement.UniformScale )
		{
			if ( !Gizmo.Control.Scale( "scale", scale.x, out var next ) ) return;

			next = Math.Max( 0.001f, next );
			PlacementDragging = true;
			Placement.SetScale( new Vector3( next ) );
			return;
		}

		if ( !Gizmo.Control.Scale( "scale", scale, out var sized, basis ) ) return;

		sized = new Vector3( Math.Max( 0.001f, sized.x ), Math.Max( 0.001f, sized.y ), Math.Max( 0.001f, sized.z ) );
		PlacementDragging = true;
		Placement.SetScale( sized );
	}
}
