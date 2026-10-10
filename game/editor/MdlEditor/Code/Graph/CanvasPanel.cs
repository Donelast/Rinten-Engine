using Rinten.UI;

namespace Editor.MdlEditor;

//Base for pannable, zoomable graph canvases; derived classes implement Fit and PaintCanvas
[StyleSheet( "CanvasPanel.cs.scss" )]
internal abstract class CanvasPanel : PaintPanel
{
	//Selection pink used across all node editors.
	protected static readonly Color SelectionColor = Color.Parse( "#ff99c8" ) ?? default;

	protected readonly Window Editor;

	//View centre in graph units.
	protected internal Vector2 ViewCenter { get; private set; }

	//Pixels per graph unit. Zero until the first fit.
	protected internal float Zoom { get; private set; }

	private Vector2 _centerTarget;
	private float _zoomTarget;
	private Vector2? _zoomAnchor;
	private Vector2 _zoomAnchorGraph;
	private bool _panning;
	private MouseButtons _panButton = MouseButtons.Middle;
	private Vector2 _lastMouse;

	//Graph-space y grows up the screen instead of down.
	protected virtual bool FlipY => false;

	//Locks the view to the x axis for one-dimensional canvases.
	protected virtual bool LockY => false;

	protected virtual float MinZoom => 0.2f;
	protected virtual float MaxZoom => 2.5f;

	//Limits may adapt to content, so never let a shifting floor shove the current view — it only stops you zooming further out from where you are.
	private float ClampZoom( float zoom ) => Math.Clamp( zoom, MathF.Min( MinZoom, _zoomTarget ), MaxZoom );

	protected CanvasPanel( Window window )
	{
		Editor = window;

		AddClass( "canvas" );

		//F fits the view, so the canvas has to be able to hold the keyboard.
		AcceptsFocus = true;
	}

	//How big the canvas is, in the units the painting is written in. Box.Rect is device pixels; a painter says
	//"sixteen" and means the same sixteen the stylesheet does, which is what ScaleFromScreen converts to.
	protected Vector2 Size => Box.Rect.Size * ScaleFromScreen;

	protected float Width => Size.x;
	protected float Height => Size.y;

	//Where the pointer is, in those same units.
	protected Vector2 LocalMouse => MousePosition * ScaleFromScreen;

	protected Vector2 ToCanvas( Vector2 p )
	{
		var d = (p - ViewCenter) * Zoom;
		return Size * 0.5f + (FlipY ? new Vector2( d.x, -d.y ) : d);
	}

	protected Vector2 FromCanvas( Vector2 s )
	{
		var d = (s - Size * 0.5f) / Zoom;
		return ViewCenter + (FlipY ? new Vector2( d.x, -d.y ) : d);
	}

	protected Rect ToCanvas( Rect r ) => new( ToCanvas( r.TopLeft ), r.Size * Zoom );

	//Frame everything - called on F, Fit View and before the first paint.
	protected abstract void Fit();

	internal void FitView() => Fit();

	protected abstract void PaintCanvas();

	//Smoothly bring the view to the given centre and zoom.
	protected void FrameView( Vector2 center, float zoom )
	{
		_centerTarget = LockY ? center.WithY( 0 ) : center;
		_zoomTarget = ClampZoom( zoom );
		_zoomAnchor = null;

		if ( Zoom <= 0 )
		{
			ViewCenter = _centerTarget;
			Zoom = _zoomTarget;
		}

		Repaint();
	}

	//Snap straight to a remembered view - used when a page reopens.
	internal void RestoreView( Vector2 center, float zoom ) => FrameView( center, zoom );

	public override void Tick()
	{
		//A pan is read here rather than only from mouse moves: the pointer leaves the canvas while the view
		//is being dragged, and a panel is told about neither a move nor a button coming up somewhere else.
		if ( _panning )
		{
			PanTo( LocalMouse );

			if ( !Application.MouseButtons.HasFlag( _panButton ) )
				EndPan();
		}

		SmoothView();

		base.Tick();
	}

	private void SmoothView()
	{
		if ( Zoom <= 0 )
			return;

		if ( ViewCenter.Distance( _centerTarget ) * Zoom < 0.5f && MathF.Abs( MathF.Log( _zoomTarget / Zoom ) ) < 0.002f )
		{
			_zoomAnchor = null;
			return;
		}

		var t = 1f - MathF.Exp( -RealTime.Delta * 20f );
		Zoom = MathF.Exp( MathX.Lerp( MathF.Log( Zoom ), MathF.Log( _zoomTarget ), t ) );

		if ( _zoomAnchor is { } anchor )
		{
			//Keep the graph point under the cursor pinned while the zoom eases in
			var d = (anchor - Size * 0.5f) / Zoom;
			ViewCenter = _zoomAnchorGraph - (FlipY ? new Vector2( d.x, -d.y ) : d);
			if ( LockY ) ViewCenter = ViewCenter.WithY( 0 );
			_centerTarget = ViewCenter;
		}
		else
		{
			ViewCenter = Vector2.Lerp( ViewCenter, _centerTarget, t );
		}

		Repaint();
	}

	protected bool IsPanning => _panning;

	//Begins dragging the view about. The button is remembered because it is not always the middle one: a
	//canvas starts a pan from a left press on empty space, and the pan has to end when THAT button comes up.
	protected void StartPan( Vector2 position, MouseButtons button = MouseButtons.Middle )
	{
		_panning = true;
		_panButton = button;
		_lastMouse = position;

		SetClass( "panning", true );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		Focus();

		if ( e.MouseButton != MouseButtons.Middle )
			return;

		StartPan( LocalMouse, MouseButtons.Middle );
		e.StopPropagation();
	}

	//Drags the view so the graph point under the pointer stays there.
	private void PanTo( Vector2 here )
	{
		var delta = (here - _lastMouse) / Zoom;
		if ( delta.IsNearZeroLength ) return;

		_lastMouse = here;

		if ( FlipY ) delta.y = -delta.y;
		if ( LockY ) delta.y = 0;

		ViewCenter -= delta;
		_centerTarget = ViewCenter;
		_zoomTarget = Zoom;
		_zoomAnchor = null;

		Repaint();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		EndPan();
	}

	//Stops panning, wherever the button came up.
	protected void EndPan()
	{
		if ( !_panning ) return;

		_panning = false;

		SetClass( "panning", false );
	}

	/// <summary>Zoom, anchored on the pointer.</summary>
	public override void OnMouseWheel( Vector2 value )
	{
		if ( value.y == 0 )
		{
			base.OnMouseWheel( value );
			return;
		}

		var at = LocalMouse;

		_zoomTarget = ClampZoom( _zoomTarget * (value.y < 0 ? 1.15f : 1 / 1.15f) );
		_zoomAnchor = at;
		_zoomAnchorGraph = FromCanvas( at );

		Repaint();
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Pressed && e.Button == "f" && !e.HasCtrl && !e.HasAlt )
		{
			Fit();
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	protected sealed override void OnPaintContent( Rect rect )
	{
		if ( Zoom <= 0 )
			Fit();

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		PaintCanvas();
	}

	//The canvas as a picture, for a drag or a thumbnail.
	internal Pixmap Capture()
	{
		if ( Zoom <= 0 )
			Fit();

		var size = Size;
		var pixmap = new Pixmap( (int)size.x, (int)size.y );

		using ( Paint.ToPixmap( pixmap ) )
		{
			Paint.Antialiasing = true;
			Paint.TextAntialiasing = true;
			PaintCanvas();
		}

		return pixmap;
	}
}
