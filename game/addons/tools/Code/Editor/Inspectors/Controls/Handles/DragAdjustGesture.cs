namespace Editor.Inspectors;

/// <summary>Turns press and sideways drag over a panel into IDragAdjustable moves; attached, not inherited</summary>
public sealed class DragAdjustGesture
{
	/// <summary>The class the panel wears while a drag is running, for styling.</summary>
	public const string DraggingClass = "dragging";

	//Held down, a pointer moves the value a tenth of what it would.
	const float FinePrecision = 0.1f;

	//And held down, ten times.
	const float CoarsePrecision = 10.0f;

	readonly Panel _panel;

	/// <summary>What the drag moves. Null for a panel that is not draggable just now.</summary>
	public IDragAdjustable Adjustable { get; set; }

	/// <summary>True between the press and the release.</summary>
	public bool IsDragging { get; private set; }

	//Where the pointer was when it was last read, in root panel space.
	float _lastX;

	public DragAdjustGesture( Panel panel )
	{
		ArgumentNullException.ThrowIfNull( panel );

		_panel = panel;

		panel.AddEventListener( "onmousedown", OnMouseDown );
		panel.AddEventListener( "onmousemove", OnMouseMove );
		panel.AddEventListener( "onmouseup", OnMouseUp );
	}

	void OnMouseDown( PanelEvent e )
	{
		if ( IsDragging || e is not MousePanelEvent mouse )
			return;

		if ( mouse.MouseButton != MouseButtons.Left )
			return;

		if ( Adjustable?.CanAdjust != true )
			return;

		IsDragging = true;
		_lastX = mouse.PointerPosition.x;

		_panel.SetClass( DraggingClass, true );
		Adjustable.BeginAdjust();

		//Kept here rather than let up the tree, where the panel that scrolls the inspector would take the same press
		//for a drag of its own - and the moment it does, the press we are holding is cancelled out from under us.
		e.StopPropagation();
	}

	void OnMouseMove( PanelEvent e )
	{
		if ( !IsDragging )
			return;

		//Sent to whatever the button went down on, so a drag carries on past the edge of the handle. If nothing is
		//pressed any more we missed the release - the panel was rebuilt under the pointer, say - and stop here.
		if ( !_panel.HasActive )
		{
			Stop();
			return;
		}

		if ( e is not MousePanelEvent mouse )
			return;

		var x = mouse.PointerPosition.x;
		var moved = x - _lastX;
		_lastX = x;

		Adjustable.Adjust( moved * Precision );

		e.StopPropagation();
	}

	void OnMouseUp( PanelEvent e )
	{
		if ( !IsDragging )
			return;

		Stop();
	}

	void Stop()
	{
		IsDragging = false;

		_panel.SetClass( DraggingClass, false );
		Adjustable?.EndAdjust();
	}

	//How much of the pointer's travel counts, which is all of it unless a modifier says otherwise.
	static float Precision
	{
		get
		{
			var modifiers = Application.KeyboardModifiers;

			if ( modifiers.HasFlag( KeyboardModifiers.Ctrl ) )
				return FinePrecision;

			if ( modifiers.HasFlag( KeyboardModifiers.Shift ) )
				return CoarsePrecision;

			return 1.0f;
		}
	}
}
