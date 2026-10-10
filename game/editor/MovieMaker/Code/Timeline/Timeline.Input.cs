using Rinten.MovieMaker;
using Rinten.UI;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

//Timeline pointer and keyboard input; hit-testing uses scene rects so half-width handles work
partial class Timeline
{
	private const float WheelPanPixels = 64f;
	private const float WheelScrollPixels = 48f;
	private const float DoubleClickTime = 0.5f;

	private Vector2 _lastMouseLocalPos;
	private Vector2 _lastMouseScenePos;

	private bool _panning;
	private Vector2? _contextMenuScenePos;

	private TimeSince _sinceClick;
	private IMovieItem? _lastClickedItem;

	public Vector2 MouseScenePos => _lastMouseScenePos;

	private MovieMouseEvent MouseEvent( MousePanelEvent e ) => new()
	{
		LocalPosition = ScreenToLocal( e.PointerPosition ),
		ScenePosition = ScreenToScene( e.PointerPosition ),
		Button = e.MouseButton,
		ButtonState = Application.MouseButtons,
		Modifiers = Application.KeyboardModifiers
	};

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Focus();

		var ev = MouseEvent( e );

		_lastMouseLocalPos = ev.LocalPosition;
		_lastMouseScenePos = ev.ScenePosition;
		_contextMenuScenePos = null;

		if ( ev.MiddleMouseButton )
		{
			_panning = true;
			e.StopPropagation();
			return;
		}

		var scenePos = ev.ScenePosition;
		var time = ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursorPanel ) );

		var accepted = false;

		//Look for an item we clicked on. If shift is pressed, let us drag a time selection instead.

		if ( !ev.HasShift && GetItemAt( scenePos ) is { } item and IMovieItem movieItem )
		{
			accepted = true;

			//Selection is handled here rather than by the panel so that ctrl can add to it. An
			//item that says it handles its own mouse events is left to do its own selecting.

			if ( !movieItem.OverridesMouseEvents )
			{
				ev.Accepted = true;
			}

			if ( !item.Selected )
			{
				var singleSelect = !movieItem.MultiSelectable || !ev.HasCtrl;

				if ( singleSelect )
				{
					DeselectAll();
				}
				else
				{
					//The current selection may hold things that cannot be multi-selected.

					foreach ( var selected in SelectedItems.ToArray() )
					{
						if ( selected is not IMovieItem { MultiSelectable: true } )
						{
							SetSelected( selected, false );
						}
					}
				}

				SetSelected( item, true );

				if ( singleSelect )
				{
					movieItem.SingleSelected();
				}

				if ( item is ITrackItem trackItem )
				{
					trackItem.Track.View.Select();
				}
			}

			//Move the playhead to be within whatever we clicked on

			if ( movieItem.SelectionTime is not null || ev.RightMouseButton )
			{
				time = movieItem.SelectionTime ?? time.Clamp( movieItem.TimeRange );
				Session.PlayheadTime = time;
			}

			if ( ev.LeftMouseButton && (StartResizing( scenePos, item ) || StartDragging( scenePos, item )) )
			{
				e.StopPropagation();
				return;
			}
		}

		if ( !accepted )
		{
			if ( ev.LeftMouseButton && GetTrackAt( scenePos ) is { } track )
			{
				track.OnPressed();
			}

			Session.EditMode?.MousePress( ev );
		}

		if ( ev.RightMouseButton )
		{
			_contextMenuScenePos = scenePos;

			if ( accepted ) return;

			ScrubBarTop.StartScrubbing( time, ev.Modifiers );
			Session.PlayheadTime = time;
		}
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		var ev = MouseEvent( e );
		var delta = ev.LocalPosition - _lastMouseLocalPos;

		try
		{
			if ( _panning && ev.ButtonState.HasFlag( MouseButtons.Middle ) )
			{
				View.Pan( delta );
				return;
			}

			if ( ev.HasShift )
			{
				UpdatePreviewTime( ev.ScenePosition );
			}

			if ( ev.ButtonState.HasFlag( MouseButtons.Left ) && IsDragging )
			{
				DragTo( ev.ScenePosition );

				//Scrub if we are dragging off the left or right edge of the timeline

				ScrubBarTop.Scrub( ev.ScenePosition, movePlayhead: false );
				return;
			}

			if ( ev.ButtonState == MouseButtons.None && !ev.HasCtrl )
			{
				UpdateHovered( ev.ScenePosition );

				if ( GetItemAt( ev.ScenePosition ) is { } item )
				{
					UpdateCursor( ev.ScenePosition, item );
					return;
				}
			}

			if ( ev.ButtonState.HasFlag( MouseButtons.Right ) )
			{
				ScrubBarTop.Scrub( ev.ScenePosition, movePlayhead: true );
			}

			Session.EditMode?.MouseMove( ev );
		}
		finally
		{
			_lastMouseLocalPos = ev.LocalPosition;
			_lastMouseScenePos = ev.ScenePosition;
		}
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		var ev = MouseEvent( e );

		_panning = false;

		UpdateSnapTargets( [] );

		var scenePos = ev.ScenePosition;
		var time = ScenePositionToTime( scenePos, new SnapOptions( source => source is not TimeCursorPanel ), showSnap: false );

		//Double click, by hand: the panel one fires for whatever panel is under the pointer, and what matters here is the item the scene rectangles say.

		if ( ev.LeftMouseButton && !ev.HasShift && !ev.HasCtrl )
		{
			var item = GetItemAt( scenePos ) as IMovieItem;

			if ( item is not null && _sinceClick < DoubleClickTime && item == _lastClickedItem )
			{
				item.DoubleClick();

				_sinceClick = float.PositiveInfinity;
				_lastClickedItem = null;

				e.StopPropagation();
				return;
			}

			_sinceClick = 0f;
			_lastClickedItem = item;
		}

		//Don't open the context menu if the right button was dragged.

		if ( ev.RightMouseButton && Equals( scenePos, _contextMenuScenePos ) )
		{
			OpenContextMenu( scenePos, time );

			e.StopPropagation();
			return;
		}

		ScrubBarTop.StopScrubbing();

		if ( IsDragging )
		{
			StopDragging();
			return;
		}

		Session.EditMode?.MouseRelease( ev );
	}

	public override void OnMouseWheel( Vector2 value )
	{
		var turn = value.y != 0f ? value.y : value.x;

		if ( turn.AlmostEqual( 0f ) )
		{
			base.OnMouseWheel( value );
			return;
		}

		var ev = new MovieWheelEvent
		{
			Delta = turn,
			ScenePosition = _lastMouseScenePos,
			Modifiers = Application.KeyboardModifiers
		};

		Session.EditMode?.MouseWheel( ev );

		if ( ev.Accepted ) return;

		if ( ev.HasShift )
		{
			PanImmediate( turn * WheelPanPixels );
			UpdatePreviewTime( _lastMouseScenePos );
			return;
		}

		if ( ev.HasCtrl )
		{
			//Pushing the wheel away is a negative turn, and zooms in.
			ZoomSmooth( -turn * 0.5f, _lastMouseScenePos.x );
			return;
		}

		if ( ev.HasAlt )
		{
			var dt = MovieTime.FromFrames( 1, Session.FrameRate );

			Session.PlayheadTime = Session.PlayheadTime.Round( dt ) - Math.Sign( turn ) * dt;

			PanToPlayheadTime();
			return;
		}

		ScrollImmediate( -turn * WheelScrollPixels );
	}

	public override void OnButtonEvent( ButtonEvent e )
	{
		base.OnButtonEvent( e );

		var ev = new MovieKeyEvent { Button = e.Button, Modifiers = e.KeyboardModifiers };

		if ( e.Pressed )
		{
			Session.EditMode?.KeyPress( ev );

			if ( ev.Accepted ) return;

			if ( e.Button == "shift" )
			{
				Session.PreviewTime = ScenePositionToTime( _lastMouseScenePos );
			}
		}
		else
		{
			Session.EditMode?.KeyRelease( ev );
		}
	}

	public void UpdatePreviewTime( Vector2 scenePos )
	{
		Session.PreviewTime = Application.MouseButtons != MouseButtons.None
			? ScenePositionToTime( scenePos )
			: PixelsToTime( scenePos.x );
	}

	public void OpenContextMenu( Vector2 scenePos, MovieTime time )
	{
		var menu = new Menu();
		var timelineTrack = Tracks.FirstOrDefault( x => x.SceneRect.IsInside( scenePos ) );
		var titleLabel = menu.AddHeading( time.ToString() );

		var ev = new EditMode.ContextMenuEvent( scenePos, time, timelineTrack, menu, titleLabel );

		if ( GetItemAt( scenePos ) is { } item and IMovieContextMenu ctxMenuItem )
		{
			ev.Title = "Selection";

			if ( !item.Selected )
			{
				SetSelected( item, true );
			}

			ctxMenuItem.ShowContextMenu( ev );
		}
		else
		{
			Session.CreateImportMenu( menu, time );
		}

		if ( !ev.Accepted )
		{
			Session.EditMode?.ContextMenu( ev );
		}

		menu.OpenAtCursor( this );
	}
}
