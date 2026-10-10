namespace Editor.Inspectors;

//One curve in the chart: its value, the part of it being looked at, and the handles that edit it.
sealed class CurveTrack
{
	/// <summary>A keyframe of the curve, and where it sits in the chart.</summary>
	public sealed class Handle
	{
		public Curve.Frame Frame;

		/// <summary>Where it is in the plot, in the chart panel's own pixels.</summary>
		public Vector2 Position;

		public bool Selected;

		/// <summary>Whether this handle's mode gives it tangents to drag at all.</summary>
		public bool HasTangents => Frame.Mode is Curve.HandleMode.Mirrored or Curve.HandleMode.Split;
	}

	/// <summary>Which of a handle's two tangents something is about.</summary>
	public enum TangentSide
	{
		None,
		In,
		Out
	}

	readonly Func<Curve> read;
	readonly Action<Curve> write;

	public CurveTrack( Func<Curve> read, Action<Curve> write )
	{
		this.read = read;
		this.write = write;

		Value = read();
	}

	Curve curve;

	/// <summary>The curve being edited. Setting it refits the viewport and rebuilds the handles.</summary>
	public Curve Value
	{
		get => curve;
		set
		{
			curve = value;
			curve.Fix();

			FitViewportToCurve();
			RebuildHandles();
		}
	}

	/// <summary>The size of the plot, in the chart panel's own pixels.</summary>
	public Vector2 Size { get; set; } = new( 100, 100 );

	/// <summary>The part of the curve the chart is looking at: (timeMin, valueMin, timeMax, valueMax).</summary>
	public Vector4 Viewport { get; set; }

	public Vector2 TimeRange => curve.TimeRange;
	public Vector2 ValueRange => curve.ValueRange;
	public Vector2 ViewportRangeX => new( Viewport.x, Viewport.z );
	public Vector2 ViewportRangeY => new( Viewport.y, Viewport.w );

	public Color CurveColor { get; set; } = Theme.Green;
	public Color SelectionColor { get; set; } = Color.White;

	/// <summary>True when this curve is one of the two edges of a <see cref="CurveRange"/>.</summary>
	public bool IsPartOfRange { get; set; }

	public bool CanEditTimeRange { get; set; } = true;
	public bool CanEditValueRange { get; set; } = true;

	/// <summary>How far a tangent grip sits from its handle, in chart pixels.</summary>
	public float HandleDistance { get; set; } = 50.0f;

	public List<Handle> Handles { get; } = new();

	/// <summary>Reads the curve back off whatever it is bound to.</summary>
	public void Reload()
	{
		var wanted = read();

		if ( wanted.Equals( curve ) ) return;

		var viewport = Viewport;

		Value = wanted;
		Viewport = viewport;

		UpdateHandlePositions();
	}

	void RebuildHandles()
	{
		Handles.Clear();

		foreach ( var frame in curve.Frames )
		{
			Handles.Add( new Handle { Frame = frame } );
		}

		UpdateHandlePositions();
	}

	public void UpdateHandlePositions()
	{
		foreach ( var handle in Handles )
		{
			handle.Position = ValueToPosition( new Vector2( handle.Frame.Time, handle.Frame.Value ) );
		}
	}

	/// <summary>Where a normalized frame sits in the plot.</summary>
	public Vector2 ValueToPosition( Vector2 value )
	{
		var x = value.x.Remap( 0, 1, TimeRange.x, TimeRange.y, false );
		var y = value.y.Remap( 0, 1, ValueRange.x, ValueRange.y, false );

		var drawnX = x.Remap( ViewportRangeX.x, ViewportRangeX.y, 0, 1, false );
		var drawnY = y.Remap( ViewportRangeY.x, ViewportRangeY.y, 0, 1, false );

		return new Vector2( drawnX, 1f - drawnY ) * Size;
	}

	/// <summary>And back: a point of the plot as a normalized frame.</summary>
	public Vector2 PositionToValue( Vector2 position )
	{
		var x = position.x.Remap( 0, Size.x, ViewportRangeX.x, ViewportRangeX.y, false );
		var y = position.y.Remap( 0, Size.y, ViewportRangeY.y, ViewportRangeY.x, false );

		if ( curve.Length > 1 )
		{
			x = x.Remap( TimeRange.x, TimeRange.y, 0, 1, false );
			y = y.Remap( ValueRange.x, ValueRange.y, 0, 1, false );
		}

		return new Vector2( x, y );
	}

	/// <summary>The time of a handle, in the curve's own units rather than 0-1.</summary>
	public float GetHandleTime( Handle handle ) => handle.Frame.Time.Remap( 0, 1, TimeRange.x, TimeRange.y, false );

	/// <summary>The value of a handle, in the curve's own units rather than 0-1.</summary>
	public float GetHandleValue( Handle handle ) => handle.Frame.Value.Remap( 0, 1, ValueRange.x, ValueRange.y, false );

	/// <summary>Puts a handle at a time and value given in the curve's own units.</summary>
	public void SetHandleValue( Handle handle, float time, float value )
	{
		var x = time.Remap( TimeRange.x, TimeRange.y, 0, 1, false );
		var y = value.Remap( ValueRange.x, ValueRange.y, 0, 1, false );

		handle.Frame.Time = x;
		handle.Frame.Value = y;
		handle.Position = ValueToPosition( new Vector2( x, y ) );

		Save();
	}

	/// <summary>Moves a handle to a point of the plot.</summary>
	public void MoveHandle( Handle handle, Vector2 position, float snapX = 0, float snapY = 0 )
	{
		position = position.Clamp( 0, Size );

		var value = PositionToValue( position );

		if ( snapX > 0 )
		{
			value.x = value.x
				.Remap( 0, 1, TimeRange.x, TimeRange.y, false )
				.SnapToGrid( snapX )
				.Remap( TimeRange.x, TimeRange.y, 0, 1, false );
		}

		if ( snapY > 0 )
		{
			value.y = value.y
				.Remap( 0, 1, ValueRange.x, ValueRange.y, false )
				.SnapToGrid( snapY )
				.Remap( ValueRange.x, ValueRange.y, 0, 1, false );
		}

		handle.Frame.Time = value.x;
		handle.Frame.Value = value.y;
		handle.Position = (snapX > 0 || snapY > 0) ? ValueToPosition( value ) : position;

		Save();
	}

	/// <summary>Where a tangent grip of a handle sits, relative to the handle itself.</summary>
	public Vector2 GetTangentOffset( Handle handle, TangentSide side )
	{
		var value = side == TangentSide.In ? handle.Frame.In : handle.Frame.Out;

		//The value is the tangent of the leaving angle, doubled; undo that for a direction.
		var angle = MathF.Atan( value / 2.0f ) + MathF.PI * 0.5f;
		var dir = Vector2.FromRadians( angle );

		if ( side == TangentSide.In ) dir.x *= -1.0f;

		dir.y *= -1.0f;

		return dir * HandleDistance;
	}

	/// <summary>Points a tangent at somewhere in the plot, relative to its handle.</summary>
	public void SetTangentFrom( Handle handle, TangentSide side, Vector2 offset )
	{
		var incoming = side == TangentSide.In;

		//Just off the vertical, so a tangent dragged past its handle does not flip sides.
		if ( !incoming && offset.x < 0.01f ) offset.x = 0.01f;
		if ( incoming && offset.x > -0.01f ) offset.x = -0.01f;
		if ( incoming ) offset.y *= -1.0f;

		var angle = MathF.Atan2( offset.x, offset.y ) + MathF.PI * 0.5f;
		var tangent = MathF.Tan( angle ) * 2.0f;

		if ( incoming ) handle.Frame.In = tangent;
		else handle.Frame.Out = tangent;

		//Mirrored means the other side is this one, backwards.
		if ( handle.Frame.Mode == Curve.HandleMode.Mirrored )
		{
			if ( incoming ) handle.Frame.Out = -tangent;
			else handle.Frame.In = -tangent;
		}

		Save();
	}

	public void SetHandleMode( Handle handle, Curve.HandleMode mode )
	{
		if ( handle.Frame.Mode == mode ) return;

		handle.Frame.Mode = mode;

		//The curve reads In and Out in every mode but Stepped and Linear.
		if ( mode is not (Curve.HandleMode.Mirrored or Curve.HandleMode.Split) )
		{
			handle.Frame.In = 0;
			handle.Frame.Out = 0;
		}

		Save();
	}

	/// <summary>Adds a handle at a point of the plot, on the curve itself.</summary>
	public Handle AddHandleAt( float localX )
	{
		var x = localX / MathF.Max( Size.x, 1 );

		x = x.Remap( 0, 1, ViewportRangeX.x, ViewportRangeX.y, false );
		x = x.Remap( TimeRange.x, TimeRange.y, 0, 1, false );

		var handle = new Handle
		{
			Frame = new Curve.Frame( x, curve.EvaluateDelta( x ) )
		};

		//Where it goes, before it is saved: the save orders the handles by it.
		handle.Position = ValueToPosition( new Vector2( handle.Frame.Time, handle.Frame.Value ) );

		Handles.Add( handle );

		Save();

		return handle;
	}

	/// <summary>Removes a handle, unless it is the last one standing.</summary>
	public bool Remove( Handle handle )
	{
		if ( Handles.Count <= 1 ) return false;
		if ( !Handles.Remove( handle ) ) return false;

		Save();

		return true;
	}

	public void FitViewportToCurve()
	{
		Viewport = new Vector4(
			TimeRange.x, ValueRange.x,
			TimeRange.y, ValueRange.y );
	}

	public void UpdateZoomRangeX( Vector2 range ) => Viewport = new Vector4( range.x, Viewport.y, range.y, Viewport.w );

	public void UpdateZoomRangeY( Vector2 range ) => Viewport = new Vector4( Viewport.x, range.x, Viewport.z, range.y );

	public void UpdateTimeRange( Vector2 range, bool retainTimes, bool fitViewport = false )
	{
		var oldViewport = Viewport;

		var c = Value;
		c.UpdateTimeRange( range, retainTimes );
		Value = c;

		if ( !fitViewport ) Viewport = oldViewport;

		Push();
	}

	public void UpdateValueRange( Vector2 range, bool retainValues, bool fitViewport = false )
	{
		var oldViewport = Viewport;

		var c = Value;
		c.UpdateValueRange( range, retainValues );
		Value = c;

		if ( !fitViewport ) Viewport = oldViewport;

		Push();
	}

	/// <summary>The curve as the plot draws it: stretched from its own range onto the viewport.</summary>
	public Curve GetViewportAdjustedCurve()
	{
		var adjusted = Value.WithFrames( Value.Frames );

		adjusted.UpdateTimeRange( ViewportRangeX, true );
		adjusted.UpdateValueRange( ViewportRangeY, true );

		return adjusted;
	}

	/// <summary>Where the curve is at a point across the plot, in chart pixels.</summary>
	public float EvaluateAtLocalX( float localX )
	{
		var adjusted = GetViewportAdjustedCurve();

		var x = (localX / MathF.Max( Size.x, 1 )).Clamp( 0, 1 );

		var f = adjusted.Evaluate( ViewportRangeX.x.LerpTo( ViewportRangeX.y, x ) );

		return f.LerpInverse( ViewportRangeY.y, ViewportRangeY.x ) * Size.y;
	}

	/// <summary>Called after anything moved. Rebuilds the curve from the handles and writes it out.</summary>
	public void Save()
	{
		WriteHandlesToCurve();

		Push();
	}

	//Writes the curve out as it stands, without rebuilding it from the handles.
	void Push()
	{
		write?.Invoke( curve );

		OnChanged?.Invoke();
	}

	/// <summary>Raised after the curve was rewritten from its handles.</summary>
	public Action OnChanged { get; set; }

	//Builds the curve back out of the handles, growing its ranges to hold whatever they were dragged to.
	void WriteHandlesToCurve()
	{
		if ( Value.Length <= 0 ) return;

		var timeRange = new Vector2( float.MaxValue, float.MinValue );
		var valueRange = new Vector2( float.MaxValue, float.MinValue );

		foreach ( var frame in Value.Frames )
		{
			var time = frame.Time.Remap( 0, 1, TimeRange.x, TimeRange.y, false );
			var value = frame.Value.Remap( 0, 1, ValueRange.x, ValueRange.y, false );

			timeRange = timeRange
				.WithX( MathF.Min( timeRange.x, time ) )
				.WithY( MathF.Max( timeRange.y, time ) );

			valueRange = valueRange
				.WithX( MathF.Min( valueRange.x, value ) )
				.WithY( MathF.Max( valueRange.y, value ) );
		}

		if ( !CanEditTimeRange || IsPartOfRange ) timeRange = TimeRange;
		if ( !CanEditValueRange || IsPartOfRange ) valueRange = ValueRange;

		var rebuilt = Value.WithFrames( new List<Curve.Frame>() );

		var timeRangeLength = timeRange.y - timeRange.x;
		var valueRangeLength = valueRange.y - valueRange.x;

		foreach ( var handle in Handles.OrderBy( x => x.Position.x ) )
		{
			rebuilt.AddPoint( handle.Frame );

			if ( timeRangeLength != 0 )
			{
				handle.Frame.Time = handle.Frame.Time
					.Remap( 0, 1, MathF.Min( TimeRange.x, TimeRange.y ), MathF.Max( TimeRange.x, TimeRange.y ), false )
					.Remap( timeRange.x, timeRange.y, 0, 1, false );
			}

			if ( valueRangeLength != 0 )
			{
				handle.Frame.Value = handle.Frame.Value
					.Remap( 0, 1, MathF.Min( ValueRange.x, ValueRange.y ), MathF.Max( ValueRange.x, ValueRange.y ), false )
					.Remap( valueRange.x, valueRange.y, 0, 1, false );
			}
		}

		if ( timeRangeLength != 0 ) rebuilt.UpdateTimeRange( timeRange, true );
		if ( valueRangeLength != 0 ) rebuilt.UpdateValueRange( valueRange, true );

		curve = rebuilt;
	}
}
