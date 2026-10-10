namespace Editor.Inspectors;
[StyleSheet( "GradientStopBarPanel.cs.scss" )]
public sealed class GradientStopBarPanel : Panel
{
	public sealed class Stop
	{
		public int Index;
		public float Time;
		public Color Value;
		public bool Disabled;
		public Panel Handle;
	}
	public Action<float> OnAddPoint;
	public Action<Stop> OnStopSelected;
	public Action OnPointsChanged;
	public Action OnPointsLiveChanged;
	public bool IsColorBar { get; set; } = true;
	public bool IsDragging => _dragging;
	public List<Stop> Points { get; } = new();
	Panel _handles;
	Stop _pressed;
	Stop _hovered;
	bool _dragging;
	bool _dragMoved;
	int _lastTrackWidth = -1;
	Vector2 _dragStartLocal;
	const float TrackInset = 8f;
	const float HandleWidth = 10f;
	const float HandleHalf = HandleWidth * 0.5f;
	public GradientStopBarPanel()
	{
		_handles = AddChild<Panel>( "handles" );
		AddEventListener( "onmousedown", OnMouseDown );
		AddEventListener( "onmousemove", OnMouseMove );
		AddEventListener( "onmouseup", OnMouseUp );
		AddEventListener( "onmouseleave", OnMouseLeave );
	}
	public Stop Selected { get; private set; }
	public void SetSelected( Stop stop )
	{
		Selected = stop;
		UpdateHandleStyles();
	}
	public void SyncPoints( IEnumerable<(float Time, Color Value)> stops )
	{
		Points.Clear();
		_handles.DeleteChildren();
		_pressed = null;
		_hovered = null;
		_dragging = false;
		_dragMoved = false;
		var index = 0;
		foreach ( var (time, value) in stops )
		{
			var stop = new Stop
			{
				Index = index++,
				Time = time,
				Value = value
			};
			stop.Handle = CreateHandle( stop );
			Points.Add( stop );
		}
		_lastTrackWidth = -1;
		UpdateHandleStyles();
		UpdateHandleStylesIfWidthChanged();
	}
	Panel CreateHandle( Stop stop )
	{
		var handle = _handles.AddChild<Panel>( "stop" );
		handle.AddClass( IsColorBar ? "color-stop" : "alpha-stop" );
		UpdateHandle( stop );
		return handle;
	}
	void UpdateHandle( Stop stop )
	{
		if ( stop.Handle is null || !stop.Handle.IsValid() )
			return;
		var trackWidth = GetTrackWidth();
		if ( trackWidth > 0 )
		{
			var center = stop.Time * trackWidth;
			stop.Handle.Style.Left = Length.Pixels( center - HandleHalf );
		}
		stop.Handle.Style.BackgroundColor = IsColorBar
			? stop.Value.WithAlpha( 1 )
			: (Color.White * MathF.Max( stop.Value.a, 0.12f )).WithAlpha( 1 );
		stop.Handle.SetClass( "selected", stop == Selected );
		stop.Handle.SetClass( "pressed", stop == _pressed );
	}
	void UpdateHandleStyles()
	{
		foreach ( var point in Points )
			UpdateHandle( point );
	}
	float GetTrackWidth()
	{
		if ( _handles.IsValid() && _handles.Box.Rect.Width > 0 )
			return _handles.Box.Rect.Width;
		var rect = Box.Rect;
		if ( rect.Width <= 0 )
			return 0;
		return rect.Width - (TrackInset * 2f);
	}
	Rect GetTrackRect()
	{
		var width = GetTrackWidth();
		var height = Box.Rect.Height;
		if ( width <= 0 || height <= 0 )
			return default;
		return new Rect( TrackInset, 0, width, height );
	}
	Vector2 EventLocal( MousePanelEvent e ) => ScreenPositionToPanelPosition( e.PointerPosition );
	Vector2 PointerLocal() => MousePosition + ScrollOffset;
	public float LocalToTime( float localX )
	{
		var track = GetTrackRect();
		if ( track.Width <= 0 )
			return 0;
		return ((localX - track.Left) / track.Width).Clamp( 0, 1 );
	}
	float TimeToLocal( float time )
	{
		var track = GetTrackRect();
		return track.Left + time * track.Width;
	}
	Stop GetStopAt( float localX, float threshold = 5f )
	{
		Stop best = null;
		var bestDist = threshold;
		foreach ( var point in Points )
		{
			var dist = MathF.Abs( TimeToLocal( point.Time ) - localX );
			if ( dist >= bestDist )
				continue;
			bestDist = dist;
			best = point;
		}
		return best;
	}
	public void UpdateHandlePositions()
	{
		UpdateHandleStyles();
	}
	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		UpdateHandleStylesIfWidthChanged();
	}
	void UpdateHandleStylesIfWidthChanged()
	{
		var trackWidth = (int)GetTrackWidth();
		if ( trackWidth == _lastTrackWidth )
			return;
		_lastTrackWidth = trackWidth;
		if ( trackWidth <= 0 )
			return;
		UpdateHandleStyles();
	}
	bool ShouldRemoveAt( Vector2 local )
	{
		var track = GetTrackRect();
		if ( track.Width <= 0 )
			return false;
		return !track.Grow( 256, 16 ).IsInside( local );
	}
	public override void Tick()
	{
		base.Tick();
		UpdateHandleStylesIfWidthChanged();
		if ( !_dragging || _pressed is null )
			return;
		if ( (Application.MouseButtons & MouseButtons.Left) == 0 )
		{
			FinishDrag( PointerLocal() );
			return;
		}
		ContinueDrag( PointerLocal() );
	}
	void ContinueDrag( Vector2 local )
	{
		if ( !_dragMoved && local.Distance( _dragStartLocal ) < 1f )
			return;
		_dragMoved = true;
		DragTo( local );
		OnPointsLiveChanged?.Invoke();
	}
	void DragTo( Vector2 local )
	{
		var track = GetTrackRect();
		if ( track.Width <= 0 )
			return;
		_pressed.Disabled = ShouldRemoveAt( local );
		_pressed.Time = LocalToTime( local.x ).Clamp( 0, 1 );
		UpdateHandle( _pressed );
	}
	void FinishDrag( Vector2 local )
	{
		if ( _pressed is null )
		{
			_dragging = false;
			_dragMoved = false;
			return;
		}
		var stop = _pressed;
		_pressed = null;
		_dragging = false;
		var remove = _dragMoved && ShouldRemoveAt( local );
		_dragMoved = false;
		if ( remove )
		{
			Points.Remove( stop );
			stop.Handle?.Delete();
			if ( Selected == stop )
				Selected = null;
		}
		foreach ( var point in Points )
			point.Disabled = false;
		OnPointsChanged?.Invoke();
		UpdateHandleStyles();
	}
	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || mpe.MouseButton != MouseButtons.Left )
			return;
		var local = EventLocal( mpe );
		var hit = GetStopAt( local.x ) ?? _hovered;
		if ( hit is not null )
		{
			hit.Disabled = false;
			_pressed = hit;
			_hovered = hit;
			_dragging = true;
			_dragMoved = false;
			_dragStartLocal = local;
			SetSelected( hit );
			OnStopSelected?.Invoke( hit );
			UpdateHandleStyles();
			e.StopPropagation();
			return;
		}
		OnAddPoint?.Invoke( LocalToTime( local.x ) );
		e.StopPropagation();
	}
	void OnMouseMove( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe )
			return;
		if ( _dragging && _pressed is not null )
		{
			ContinueDrag( EventLocal( mpe ) );
			e.StopPropagation();
			return;
		}
		var local = EventLocal( mpe );
		var hovered = GetStopAt( local.x );
		if ( _hovered != hovered )
		{
			_hovered = hovered;
			UpdateHandleStyles();
		}
		SetClass( "over-stop", _hovered is not null );
	}
	void OnMouseUp( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || mpe.MouseButton != MouseButtons.Left )
			return;
		if ( _pressed is null )
			return;
		FinishDrag( EventLocal( mpe ) );
		e.StopPropagation();
	}
	void OnMouseLeave( PanelEvent e )
	{
		if ( _dragging )
			return;
		if ( _hovered is not null )
		{
			_hovered = null;
			UpdateHandleStyles();
		}
	}
}
