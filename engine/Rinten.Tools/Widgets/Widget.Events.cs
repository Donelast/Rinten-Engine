using System;

namespace Editor
{
	public partial class Widget : EditorObject
	{
		#region mouse events


		static Widget _currentPressedWidget;
		internal static Widget CurrentlyPressedWidget
		{
			get => _currentPressedWidget;
			set
			{
				if ( _currentPressedWidget == value ) return;

				if ( _currentPressedWidget.IsValid() ) _currentPressedWidget.Update();
				_currentPressedWidget = value;
				if ( _currentPressedWidget.IsValid() ) _currentPressedWidget.Update();
			}
		}

		/// <summary>Whether this widget is currently being pressed down or not.</summary>
		public bool IsPressed => CurrentlyPressedWidget == this;

		//Mouse wheel was scrolled while the mouse cursor was over this widget.
		protected virtual void OnMouseWheel( WheelEvent e )
		{
#pragma warning disable CS0618 //Type or member is obsolete
			OnWheel( e );
#pragma warning restore CS0618 //Type or member is obsolete
		}

		//Mouse wheel was scrolled while the mouse cursor was over this widget.
		[Obsolete( $"Use {nameof( OnMouseWheel )}" )]
		protected virtual void OnWheel( WheelEvent e )
		{

		}

		Vector2 startClickPos;

		public Action MouseRelease;

		//Called when mouse is released over this widget.
		protected virtual void OnMouseReleased( MouseEvent e )
		{
			MouseRelease?.Invoke();

			//Clicks are decided in Widget.PanelMouseRelease, not here
		}

		public Action MouseClick;

		internal void InternalMouseClickEvent( MouseEvent e )
		{
			Application.OnWidgetClicked?.Invoke( this, e );
			if ( e.Accepted ) return;

			OnMouseClick( e );
		}

		//Called when this widget is left clicked (on mouse release).
		protected virtual void OnMouseClick( MouseEvent e )
		{
			MouseClick?.Invoke();
		}

		public Action MouseRightClick;

		//Called when this widget is right clicked (on mouse release).
		protected virtual void OnMouseRightClick( MouseEvent e )
		{
			MouseRightClick?.Invoke();
		}


		/// <summary>Called when this widget is left clicked (on mouse press).</summary>
		public Action MouseLeftPress;

		/// <summary>Called when this widget is right clicked (on mouse press).</summary>
		public Action MouseRightPress;

		/// <summary>Called when this widget is clicked with the mouse wheel (on mouse press).</summary>
		public Action MouseMiddlePress;

		//Called when mouse is pressed over this widget.
		protected virtual void OnMousePress( MouseEvent e )
		{
			//Where the press started, so the release can tell a click from a drag that happened to end here.
			if ( e.LeftMouseButton ) startClickPos = e.LocalPosition;

			_isDragStart = false;

			if ( e.LeftMouseButton && MouseLeftPress != null )
			{
				e.Accepted = true;
				MouseLeftPress?.Invoke();
			}

			if ( e.RightMouseButton && MouseRightPress != null )
			{
				e.Accepted = true;
				MouseRightPress?.Invoke();
			}

			if ( e.MiddleMouseButton && MouseMiddlePress != null )
			{
				e.Accepted = true;
				MouseMiddlePress?.Invoke();
			}

		}

		/// <summary>Whether this widget can be drag and dropped onto other widgets.</summary>
		public bool IsDraggable { get; set; }

		public Action<Vector2> MouseMove;

		//Protect against calling OnDragStart multiple times
		bool _isDragStart;

		//Called when the mouse cursor is moved while being over this widget.
		protected virtual void OnMouseMove( MouseEvent e )
		{
			//20px, or a third of a small widget, so tiny widgets can still start a drag
			var threshold = MathF.Min( 20.0f, MathF.Min( Size.x, Size.y ) / 3.0f );

			if ( IsDraggable && startClickPos.Distance( e.LocalPosition ) > threshold && (e.ButtonState & MouseButtons.Left) != 0 && !_isDragStart )
			{
				_isDragStart = true;
				OnDragStart();
			}

			MouseMove?.Invoke( e.LocalPosition );
		}

		//Mouse cursor entered the bounds of this widget.
		protected virtual void OnMouseEnter()
		{
			Update();
		}

		//Mouse cursor exited the bounds of this widget.
		protected virtual void OnMouseLeave()
		{
			Update();
		}

		//Called after OnMouseRightClick, for the purposes of opening a context menu.
		protected virtual void OnContextMenu( ContextMenuEvent e )
		{
		}

		//Called when the widget was double clicked with any mouse button.
		protected virtual void OnDoubleClick( MouseEvent e )
		{
			e.IsDoubleClick = true;
			OnMousePress( e );
		}

		#endregion

		#region keyboard events

		//A key has been pressed. Your widget needs keyboard focus for this to be called - see FocusMode.
		protected virtual void OnKeyPress( KeyEvent e )
		{
			if ( ProvidesDebugMode && e.HasAlt && e.Key == KeyCode.Pause )
			{
				DebugModeEnabled = !DebugModeEnabled;
			}
		}

		//A key has been released.
		protected virtual void OnKeyRelease( KeyEvent e )
		{

		}

		//A shortcut has been activated. This is called on the focused control so they can override it.
		protected virtual void OnShortcutPressed( KeyEvent e )
		{

		}

		//Called when the widget gains keyboard focus.
		internal event Action<FocusChangeReason> Focused;

		//Called when the widget gains keyboard focus.
		protected virtual void OnFocus( FocusChangeReason reason )
		{
			Focused?.Invoke( reason );
		}
		internal void InternalFocusInEvent( FocusChangeReason reason ) => OnFocus( reason );

		//Called when the widget loses keyboard focus.
		internal event Action<FocusChangeReason> Blurred;

		//Called when the widget loses keyboard focus.
		protected virtual void OnBlur( FocusChangeReason reason )
		{
			Blurred?.Invoke( reason );
		}
		internal void InternalFocusOutEvent( FocusChangeReason reason ) => OnBlur( reason );

		#endregion

		//Called when the widgets' size was changed.
		protected virtual void OnResize()
		{

		}

		/// <summary>Called when the widget is moved to a new position relative to it's parent.</summary>
		public event Action Moved;

		//Called when the widget was moved to a new position relative to it's parent.
		protected virtual void OnMoved()
		{
			Moved?.Invoke();
		}

		/// <summary>Override the widget's paint process. Return <see langword="true"/> to prevent the default paint action, which is to call <see cref="OnPaint"/>.</summary>
		public Func<bool> OnPaintOverride;

		//Override to custom paint your widget, for example using Paint. Can be overwritten by OnPaintOverride.
		protected virtual void OnPaint()
		{
		}

		//Called when a window is about to be closed. Answer false to keep it open - the close is the asker's to abandon.
		protected virtual bool OnClose()
		{
			return true;
		}

		//Asks whether this window may close; called by the title bar's close button
		internal bool AskClose() => OnClose();

		//Called when a window is closed.
		protected virtual void OnClosed()
		{

		}


		//on reparenting.
		internal virtual void OnWinIdChanged()
		{
		}

		internal bool InternalFocusNextPrevChild( bool next )
		{
			if ( next ) return FocusNext();
			return FocusPrevious();
		}

		/// <summary>Called when the visibility of this widget changes.</summary>
		public event Action<bool> VisibilityChanged;

		//Called when the visibility of this widget changes.
		protected virtual void OnVisibilityChanged( bool visible )
		{
			VisibilityChanged?.Invoke( visible );
		}

		//Called when Tab is pressed to find the next widget to focus. Return true to prevent focusing.
		protected virtual bool FocusNext()
		{
			return false;
		}

		//Called when Shift + Tab is pressed to find the next widget to focus. Return true to prevent focusing.
		protected virtual bool FocusPrevious()
		{
			return false;
		}

		//Called to make sure all child panels are in correct positions and have correct sizes. This
		//is typically called when the size of this widget changes, but there are other cases as well.
		protected virtual void DoLayout()
		{

		}

		internal Vector3 InternalSizeHint( int w, int h )
		{
			_baseSizeHint.x = w;
			_baseSizeHint.y = h;

			return SizeHint();
		}

		internal Vector3 InternalMinimumSizeHint( int w, int h )
		{
			_baseMiniumSizeHint.x = w;
			_baseMiniumSizeHint.y = h;

			var s = MinimumSizeHint();

			//enforce limits
			if ( MinimumWidth > 0 ) s.x = MathF.Max( s.x, MinimumWidth );
			if ( MinimumHeight > 0 ) s.y = MathF.Max( s.y, MinimumHeight );
			if ( MaximumHeight > 0 ) s.y = MathF.Min( s.y, MaximumHeight );
			if ( MaximumWidth > 0 ) s.x = MathF.Min( s.x, MaximumWidth );

			return s;
		}

		Vector2 _baseMiniumSizeHint;

		//Return the minimum size this widget wants to be
		protected virtual Vector2 MinimumSizeHint()
		{
			return _baseMiniumSizeHint;
		}

		Vector2 _baseSizeHint;

		//Should return the size this widget really wants to be if it can its way. The
		//default is that you don't care - and just to return whatever the base value is.
		protected virtual Vector2 SizeHint()
		{
			return _baseSizeHint;
		}

		/// <summary>The size hint, for whoever is arranging this widget.</summary>
		public Vector2 WantedSize()
		{
			var hint = SizeHint();

			return new Vector2(
				hint.x > 0 ? hint.x : Size.x,
				hint.y > 0 ? hint.y : Size.y );
		}

		//Whether this widget has a size of its own to ask for on an axis.
		internal bool HasSizeHint( bool vertical )
		{
			var hint = SizeHint();

			return (vertical ? hint.y : hint.x) > 0;
		}

		//The least room on an axis: property floors and minimum size hint.
		internal float MinimumWanted( bool vertical )
		{
			var hint = MinimumSizeHint();
			var fromHint = vertical ? hint.y : hint.x;
			var fromProp = vertical ? MinimumHeight : MinimumWidth;

			return MathF.Max( fromProp, fromHint );
		}

		#region drag and drop events

		private bool _beingDroppedOn;

		/// <summary>Whether something is being dragged over this widget.</summary>
		public bool IsBeingDroppedOn
		{
			get => _beingDroppedOn;

			internal set
			{
				if ( _beingDroppedOn == value ) return;
				_beingDroppedOn = value;
				Update();
			}
		}

		/// <summary>Information about a widget drag and drop event.</summary>
		public class DragEvent
		{
			internal Action<DropAction> setDropAction;

			/// <summary>Cursor position, local to this widget.</summary>
			public Vector2 LocalPosition { get; set; }

			/// <summary>The drag data.</summary>
			public DragData Data { get; internal set; }

			internal DropAction _action; //ignore default

			/// <summary>The keyboard modifier keys that were held down at the moment the event triggered.</summary>
			public KeyboardModifiers KeyboardModifiers { get; set; }

			/// <summary>Whether <c>Shift</c> key was being held down at the time of the event.</summary>
			public bool HasShift => KeyboardModifiers.Contains( KeyboardModifiers.Shift );

			/// <summary>Whether <c>Control</c> key was being held down at the time of the event.</summary>
			public bool HasCtrl => KeyboardModifiers.Contains( KeyboardModifiers.Ctrl );

			/// <summary>Whether <c>Alt</c> key was being held down at the time of the event.</summary>
			public bool HasAlt => KeyboardModifiers.Contains( KeyboardModifiers.Alt );

			/// <summary>Set this to what action will be (or was) performed.</summary>
			public DropAction Action
			{
				get => _action;
				set
				{
					_action = value;
					setDropAction?.Invoke( value );
				}
			}
		}

		//Called when dragging. IsDraggable should be true.
		protected virtual void OnDragStart()
		{

		}

		/// <summary>Cursor with drag and drop data left the bounds of this widget. Requires <see cref="AcceptDrops"/> to function.</summary>
		public virtual void OnDragLeave()
		{

		}

		/// <summary>Cursor with drag and drop data moved on this widget. Requires <see cref="AcceptDrops"/> to function.</summary>
		public virtual void OnDragHover( DragEvent ev )
		{

		}

		/// <summary>Something was dragged and dropped on this widget. Apply the data here, if its valid. Requires <see cref="AcceptDrops"/> to function.</summary>
		public virtual void OnDragDrop( DragEvent ev )
		{

		}

		#endregion
	}

	/// <summary>Describes why a <see cref="Widget"/>s' keyboard focus has changed via <see cref="Widget.OnFocus"/> and <see cref="Widget.OnBlur"/> callbacks.</summary>
	public enum FocusChangeReason
	{
		Mouse,
		Tab,
		Backtab,
		ActiveWindow,
		Popup,
		Shortcut,
		MenuBar,
		Other,
		None
	}
}
