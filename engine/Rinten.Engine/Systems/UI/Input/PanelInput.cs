using NativeEngine;
using Rinten.Engine;
using System.Runtime.InteropServices;

namespace Rinten.UI;

internal class PanelInput
{
	/// <summary>Panel we're currently hovered over</summary>
	public Panel Hovered { get; private set; }

	/// <summary>Panel we're currently pressing down</summary>
	public Panel Active { get; private set; }

	//During a drag, the panel currently under the cursor (potential drop target)
	internal Panel DropTarget { get; private set; }

	//public string LastCursor;

	public Selection Selection = new Selection();

	public PanelInput()
	{
		MouseStates = new MouseButtonState[5];

		for ( int i = 0; i < 5; i++ )
		{
			MouseStates[i] = new MouseButtonState( this, ButtonCode.MouseLeft + i );
		}
	}

	internal void Clear()
	{
		Hovered = null;
		Active = null;
		Selection = new Selection();

		foreach ( var state in MouseStates )
		{
			state.Active = null;
			state.DragTarget = null;
		}
	}

	internal virtual void Tick( IReadOnlyList<RootPanel> panels, bool mouseIsActive )
	{
		bool hoveredAny = false;

		//When we're ticking inputs, let's emulate the mouse if we're using a gamepad
		if ( Input.EnableVirtualCursor && Input.CurrentController is { } controller )
		{
			var moveX = controller.GetAxis( NativeEngine.GameControllerAxis.LeftX );
			var moveY = controller.GetAxis( NativeEngine.GameControllerAxis.LeftY );

			if ( MathF.Abs( moveX ) > 0 || MathF.Abs( moveY ) > 0 )
			{
				var screen = Screen.Size;
				var min = MathF.Min( screen.x, screen.y );
				Mouse.Position += new Vector2( moveX * min, moveY * min ) * Preferences.ControllerAnalogSpeed * RealTime.Delta;
			}
		}

		var inputData = GetInputData();

		if ( mouseIsActive )
		{
			for ( var i = 0; i < panels.Count; i++ )
			{
				var panel = panels[i];

				if ( UpdateMouse( panel, inputData ) )
				{
					hoveredAny = true;
					break;
				}
			}
		}

		if ( !hoveredAny )
		{
			SetHovered( null );
			ClearDropTarget();
		}
	}

	HashSet<ButtonCode> mousebuttons = new HashSet<ButtonCode>();
	Vector2 mouseWheelValue { get; set; }

	/// <summary>Called from input when mouse wheel changes</summary>
	public void AddMouseWheel( Vector2 value, KeyboardModifiers modifiers )
	{
		//Windows apps will typically translate vertical mouse wheel movement into horizontal mouse wheel movement if the
		//shift key is held down during a mouse wheel event This is also inverted, i.e. scrolling down will scroll to the right
		if ( modifiers.Contains( KeyboardModifiers.Shift ) )
			value = value.WithX( -value.y ).WithY( 0 );

		mouseWheelValue -= value;
	}

	//Called from input when mouse wheel changes
	internal void AddMouseButton( ButtonCode code, bool down, KeyboardModifiers modifiers )
	{
		if ( down ) mousebuttons.Add( code );
		else mousebuttons.Remove( code );
	}

	//The pointer as somebody other than the window system reports it.
	internal struct Pointer
	{
		/// <summary>Where it is, in the pixels of whatever the game is drawn into.</summary>
		public Vector2 Position;

		public bool Left;
		public bool Right;
		public bool Middle;

		/// <summary>How far the wheel has turned since it was last read.</summary>
		public Vector2 Wheel;
	}

	//Pointer forwarded by the host (editor viewport) when the game has no window of its own; else null
	internal static Pointer? External;

	internal virtual InputData GetInputData()
	{
		if ( External is { } handed )
		{
			var outside = new InputData
			{
				MousePos = handed.Position,
				Mouse0 = handed.Left,
				Mouse1 = handed.Middle,
				Mouse2 = handed.Right,
				MouseWheel = handed.Wheel,
			};

			//The wheel is how far it turned, not where it is: read once and gone, the same as the one below.
			handed.Wheel = 0;
			External = handed;

			return outside;
		}

		var mouseWheel = mouseWheelValue;
		var leftMouseDown = mousebuttons.Contains( ButtonCode.MouseLeft );

		//When using a controller, simulate left mouse click, and analog scroll wheel
		if ( Input.EnableVirtualCursor && Input.CurrentController is { } controller )
		{
			leftMouseDown |= InputRouter.IsButtonDown( GamepadCode.A );

			const float scrollScale = 0.5f;

			var mouseWheelY = controller.GetAxis( GameControllerAxis.RightY, 0 ) * scrollScale;
			var mouseWheelX = controller.GetAxis( GameControllerAxis.RightX, 0 ) * scrollScale;

			if ( MathF.Abs( mouseWheelX ) > 0f ) mouseWheel.x = mouseWheelX;
			if ( MathF.Abs( mouseWheelY ) > 0f ) mouseWheel.y = mouseWheelY;
		}

		var d = new InputData();
		d.MousePos = Mouse.Position;
		d.Mouse0 = leftMouseDown;
		d.Mouse1 = mousebuttons.Contains( ButtonCode.MouseMiddle );
		d.Mouse2 = mousebuttons.Contains( ButtonCode.MouseRight );
		d.Mouse3 = mousebuttons.Contains( ButtonCode.MouseBack );
		d.Mouse4 = mousebuttons.Contains( ButtonCode.MouseForward );
		d.MouseWheel = mouseWheel;

		mouseWheelValue = 0;

		return d;
	}

	/// <summary>The cursor should change. Name could be null, meaning default.</summary>
	public virtual void SetCursor( string name ) => Mouse.CursorType = name;

	//Whether the last hit test still answers for this frame, so the walk over every visible panel can be left out. False
	//here: a game tests every frame. A window's input overrides it, and says when nothing that decides the answer has moved.
	private protected virtual bool HoverUnchanged( RootPanel root, InputData data ) => false;

	//What the last hit test that ran found: whether the pointer was over this root at all.
	bool lastHoverFound;

	internal virtual bool UpdateMouse( RootPanel root, InputData data )
	{
		root.MousePos = data.MousePos;

		//The walk, unless whoever owns this input can say its answer is what it was. Hovered and the drop target then
		//stand from the last walk, and the buttons, the wheel and the selection below are worked out against them.
		if ( !HoverUnchanged( root, data ) )
			lastHoverFound = UpdateHovered( root, data.MousePos );

		if ( !lastHoverFound )
			return false;

		var leftMousePressed = !MouseStates[0].Pressed && data.Mouse0;
		var leftMouseReleased = MouseStates[0].Pressed && !data.Mouse0;

		MouseStates[0].Update( data.Mouse0, Hovered );
		MouseStates[1].Update( data.Mouse2, Hovered );
		MouseStates[2].Update( data.Mouse1, Hovered );
		MouseStates[3].Update( data.Mouse3, Hovered );
		MouseStates[4].Update( data.Mouse4, Hovered );

		Active = null;
		if ( MouseStates[2].Active != null ) Active = MouseStates[2].Active;
		if ( MouseStates[1].Active != null ) Active = MouseStates[1].Active;
		if ( MouseStates[0].Active != null ) Active = MouseStates[0].Active;

		if ( Hovered != null )
		{
			if ( data.MouseWheel != Vector2.Zero )
			{
				Hovered.OnMouseWheel( data.MouseWheel );
			}
		}

		Selection.UpdateSelection( root, Hovered, data.Mouse0, leftMousePressed, leftMouseReleased, data.MousePos );

		return true;
	}

	bool UpdateHovered( Panel panel, Vector2 pos )
	{
		Panel current = null;

		if ( !CheckHover( panel, pos, ref current ) )
		{
			return false;
		}

		//During a drag the hovered panel is the drop target; check Pressed so a stale drag flag can't stick
		if ( MouseStates[0].Dragged && MouseStates[0].Pressed )
		{
			UpdateDropTarget( current );
			return true;
		}

		SetHovered( current );

		return true;
	}

	internal void SetHovered( Panel current )
	{
		if ( current != Hovered )
		{
			if ( Hovered != null )
			{
				Panel.Switch( PseudoClass.Hover, false, Hovered, current );
				Hovered.CreateEvent( new MousePanelEvent( "onmouseout", Hovered, "none" ) );
			}

			Hovered = current;

			if ( Hovered != null )
			{
				if ( Active == null || Active == Hovered )
					Panel.Switch( PseudoClass.Hover, true, Hovered );

				Hovered.CreateEvent( new MousePanelEvent( "onmouseover", Hovered, "none" ) );
			}
		}
		else if ( Hovered is not null && !Hovered.HasHovered && ( Active is null || Active == Hovered ) )
		{
			//Re-apply hover after a press ends without the pointer moving, or the panel stays unhovered
			Panel.Switch( PseudoClass.Hover, true, Hovered );
		}

		var cursor = Hovered?.ComputedStyle?.Cursor;

		if ( cursor != null )
		{
			SetCursor( cursor );
			_uiClaimedCursor = true;
		}
		else if ( _uiClaimedCursor )
		{
			SetCursor( null );
			_uiClaimedCursor = false;
		}
	}

	bool _uiClaimedCursor;

	void UpdateDropTarget( Panel current )
	{
		if ( current == DropTarget )
			return;

		var dragSource = MouseStates[0].DragTarget;

		DropTarget?.CreateEvent( new PanelEvent( "ondragleave", dragSource ) );
		DropTarget = current;
		DropTarget?.CreateEvent( new PanelEvent( "ondragenter", dragSource ) );
	}

	void ClearDropTarget()
	{
		if ( DropTarget is null )
			return;

		DropTarget.CreateEvent( new PanelEvent( "ondragleave", MouseStates[0].DragTarget ) );
		DropTarget = null;
	}

	bool CheckHover( Panel panel, Vector2 pos, ref Panel current )
		=> Search( panel, pos, null, null, ref current );

	//Hit test for hover: deepest panel accepting pointer events, using draw transforms and render order
	internal static bool Search( Panel panel, Vector2 pos, System.Func<Panel, bool> skip, System.Func<Panel, bool> selectable, ref Panel current )
	{
		bool found = false;

		if ( !panel.IsVisible )
			return false;

		if ( panel.ComputedStyle == null )
			return false;

		if ( skip is not null && skip( panel ) )
			return false;

		//Transform using this panel's local matrix
		pos = panel.GetTransformPosition( pos );

		var inside = panel.IsInside( pos );

		var counts = selectable is null
			? panel.ComputedStyle.PointerEvents != PointerEvents.None
			: selectable( panel );

		if ( inside && counts )
		{
			current = panel;
			found = true;
		}

		//If we're outside and this panel has overflow hidden we can avoid testing against the children
		if ( !inside && (panel.ComputedStyle?.Overflow ?? OverflowMode.Visible) != OverflowMode.Visible )
		{
			return found;
		}

		//No children
		if ( panel._renderChildren is null || panel._renderChildren.Count == 0 )
		{
			return found;
		}

		int topIndex = -10000;

		foreach ( var child in CollectionsMarshal.AsSpan( panel._renderChildren ) )
		{
			var index = child.GetRenderOrderIndex();
			if ( index < topIndex ) continue;

			if ( Search( child, pos, skip, selectable, ref current ) )
			{
				topIndex = index;
				found = true;
			}
		}

		return found;
	}

	internal class MouseButtonState
	{
		public PanelInput Input { get; init; }
		public ButtonCode MouseButton { get; init; }

		public bool Pressed;
		public Panel Active;
		public bool Dragged;

		MousePanelEvent MouseDownEvent;

		/// <summary>Then panel that is potentially being dragged</summary>
		public Panel DragTarget;

		/// <summary>The point where we first pressed on the Active element</summary>
		public Vector2 StartHoldOffsetLocal;
		public Vector2 StartHoldOffsetScreen;

		public MouseButtonState( PanelInput input, ButtonCode i )
		{
			Input = input;
			MouseButton = i;
		}

		public void Update( bool down, Panel hovered )
		{
			var mouseMoved = !Mouse.Delta.IsNearZeroLength;

			//Watch drag - we might have started dragging
			if ( Pressed && down && DragTarget != null && mouseMoved && MouseDownEvent.Propagate )
			{
				var delta = StartHoldOffsetLocal - (DragTarget.MousePosition + DragTarget.ScrollOffset);

				if ( delta.Length > 5.0f && !Dragged )
				{
					Dragged = true;
					DragTarget?.CreateEvent( new DragEvent( "ondragstart", DragTarget, StartHoldOffsetLocal, StartHoldOffsetScreen ) );

					//The drag-start handler is user code and may rebuild or delete the panel that received the mouse-down
					//event. Do not dispatch another mouse event to an invalid panel after that callback returns.
					if ( !Active.IsValid() )
					{
						Active = null;
						DragTarget = null;
						return;
					}

					//We started dragging - stop active panel being active, no click events
					{
						Panel.Switch( PseudoClass.Active, false, Active );
						Panel.Switch( PseudoClass.Hover, false, Active );
						Active.CreateEvent( new MousePanelEvent( "onmouseup", Active, GetMouseButtonName( MouseButton ) ) );
						Active.OnButtonEvent( new ButtonEvent( MouseButton, false ) );
						Active = null;
					}
				}

				if ( Dragged )
				{
					DragTarget?.CreateEvent( new DragEvent( "ondrag", DragTarget, StartHoldOffsetLocal, StartHoldOffsetScreen ) { MouseDelta = Mouse.Delta } );
				}
			}

			if ( Pressed == down ) return;
			Pressed = down;

			if ( down ) OnPressed( hovered );
			else OnReleased( hovered );
		}

		string GetMouseButtonName( ButtonCode bc )
		{
			if ( bc == ButtonCode.MouseLeft ) return "mouseleft";
			if ( bc == ButtonCode.MouseRight ) return "mouseright";
			if ( bc == ButtonCode.MouseMiddle ) return "mousemiddle";
			if ( bc == ButtonCode.MouseBack ) return "mouseback";
			if ( bc == ButtonCode.MouseForward ) return "mouseforward";

			return bc.ToString().ToLower();
		}

		void OnPressed( Panel hovered )
		{
			if ( MouseButton == ButtonCode.MouseBack )
			{
				hovered?.CreateEvent( new PanelEvent( "onback", hovered ) );
				hovered?.OnButtonEvent( new ButtonEvent( MouseButton, true ) );
				return;
			}

			if ( MouseButton == ButtonCode.MouseForward )
			{
				hovered?.CreateEvent( new PanelEvent( "onforward", hovered ) );
				hovered?.OnButtonEvent( new ButtonEvent( MouseButton, true ) );
				return;
			}

			Active = hovered;

			if ( Panel.SuppressPopupDismissal?.Invoke( hovered ) != true )
			{
				IMenuDll.Current?.ClosePopups( hovered );
				IGameInstanceDll.Current?.ClosePopups( hovered );
				Panel.PopupPressedHandler?.Invoke( hovered );
			}

			if ( Active == null )
			{
				//A press over nothing can't drag - clear the last press's target or the drag watch dereferences a null Active
				Dragged = false;
				DragTarget = null;
				return;
			}

			Panel.Switch( PseudoClass.Active, true, Active );

			if ( MouseButton == ButtonCode.MouseLeft || MouseButton == ButtonCode.MouseRight )
			{
				Dragged = false;
				DragTarget = Active.FindDragTarget();

				if ( DragTarget != null )
				{
					StartHoldOffsetLocal = DragTarget.MousePosition + DragTarget.ScrollOffset;
					StartHoldOffsetScreen = Mouse.Position;
				}
			}

			InputFocus.SetFromPress( Active );

			MouseDownEvent = new MousePanelEvent( "onmousedown", Active, GetMouseButtonName( MouseButton ) );
			Active.CreateEvent( MouseDownEvent );

			Active.OnButtonEvent( new ButtonEvent( MouseButton, true ) );
		}

		void OnReleased( Panel hovered )
		{
			if ( MouseButton == ButtonCode.MouseBack || MouseButton == ButtonCode.MouseForward )
			{
				hovered?.OnButtonEvent( new ButtonEvent( MouseButton, false ) );
				return;
			}

			bool canClick = hovered == Active && !Dragged;

			try
			{
				if ( Dragged && DragTarget is { IsValid: true } )
				{
					DragTarget.CreateEvent( new DragEvent( "ondragend", DragTarget, StartHoldOffsetLocal, StartHoldOffsetScreen ) );

					if ( Input.DropTarget is { IsValid: true } )
					{
						Input.DropTarget.CreateEvent( new PanelEvent( "ondrop", DragTarget ) );
					}
				}
			}
			finally
			{
				//Always clear the drag, even if the dragged panel was deleted, or hover stays frozen
				Input.ClearDropTarget();

				Dragged = default;
				DragTarget = default;
				StartHoldOffsetLocal = default;
				StartHoldOffsetScreen = default;
			}

			if ( Active == null )
				return;

			if ( canClick )
			{
				Active.CreateEvent( new MousePanelEvent( "onmouseup", Active, GetMouseButtonName( MouseButton ) ) );

				if ( MouseButton == ButtonCode.MouseLeft )
				{
					Active.CreateEvent( new MousePanelEvent( "onclick", Active, GetMouseButtonName( MouseButton ) ) );
				}
				else if ( MouseButton == ButtonCode.MouseMiddle )
				{
					Active.CreateEvent( new MousePanelEvent( "onmiddleclick", Active, GetMouseButtonName( MouseButton ) ) );
				}
				else if ( MouseButton == ButtonCode.MouseRight )
				{
					Active.CreateEvent( new MousePanelEvent( "onrightclick", Active, GetMouseButtonName( MouseButton ) ) );
				}
			}
			else
			{
				Active.CreateEvent( new MousePanelEvent( "onmouseup", Active, GetMouseButtonName( MouseButton ) ) );
				Panel.Switch( PseudoClass.Hover, false, Active, hovered );
			}

			Panel.Switch( PseudoClass.Active, false, Active );

			Active.OnButtonEvent( new ButtonEvent( MouseButton, false ) );
			Active = null;
		}
	}

	internal MouseButtonState[] MouseStates;
}
