namespace Editor.Inspectors;

//The chart: axes and grid, the curves on it, and the handles that edit them.
[StyleSheet( "CurveChartPanel.cs.scss" )]
sealed class CurveChartPanel : Panel
{
	/// <summary>Room down the left for the value labels, in style pixels.</summary>
	public const float LeftGutter = 100f;

	/// <summary>Room along the bottom for the time labels, in style pixels.</summary>
	public const float BottomGutter = 26f;

	const int TicksX = 8;
	const int TicksY = 8;

	//How near the pointer has to be to grab something, in style pixels.
	const float GrabDistance = 8f;

	//And how near to the curve itself to put a new handle on it.
	const float CurveGrabDistance = 10f;

	enum Drag
	{
		None,
		Handle,
		Tangent,
		Pan
	}

	public List<CurveTrack> Tracks { get; } = new();

	/// <summary>Whether the two curves in here are the edges of one range.</summary>
	public bool IsRange { get; private set; }

	/// <summary>Whether panning and zooming moves the curve's range with the view.</summary>
	public bool MoveRangeWhenPanning { get; set; }

	/// <summary>Whether setting a min or a max keeps the values where they are on screen.</summary>
	public bool MoveRangeWhenSettingMinMax { get; set; }

	/// <summary>Raised whenever a curve was edited, so whatever holds this can write it out.</summary>
	public Action OnEdited { get; set; }

	/// <summary>Raised when the view or a range changed, so the min and max fields can catch up.</summary>
	public Action OnViewChanged { get; set; }

	readonly CurveHandlePopup popup;
	readonly PlotPanel plotPanel;
	readonly List<Rinten.UI.Label> xLabels = new();
	readonly List<Rinten.UI.Label> yLabels = new();

	readonly List<Vector2> scratch = new();
	readonly List<Vector2> scratchB = new();
	readonly List<Vector2> scratchC = new();

	//Where the plot's own drawing lands while it is being drawn.
	CurveDraw.Canvas canvas;

	Vector2Int painted;
	bool plotDirty = true;

	CurveTrack hoveredTrack;
	CurveTrack.Handle hoveredHandle;
	CurveTrack.TangentSide hoveredTangent;

	CurveTrack dragTrack;
	CurveTrack.Handle dragHandle;
	CurveTrack.TangentSide dragTangent;
	Drag dragMode;
	Vector2 dragLast;

	Vector2 lastPlotSize;

	public CurveChartPanel()
	{
		AcceptsFocus = true;

		plotPanel = new PlotPanel( this );
		AddChild( plotPanel );

		popup = AddChild<CurveHandlePopup>();
		popup.SetClass( "hidden", true );

		AddEventListener( "onmousedown", OnMouseDown );
		AddEventListener( "onmousemove", OnMouseMove );
		AddEventListener( "onmouseup", OnMouseUp );
		AddEventListener( "onmouseleave", OnMouseLeave );
	}

	/// <summary>The plot, in this panel's own pixels.</summary>
	public Rect PlotRect
	{
		get
		{
			var size = Box.Rect.Size;
			var scale = ScaleToScreen;

			var left = LeftGutter * scale;
			var bottom = BottomGutter * scale;

			return new Rect(
				left, 0,
				MathF.Max( size.x - left, 1 ),
				MathF.Max( size.y - bottom, 1 ) );
		}
	}

	//The same rectangle where the draw list wants it - offset by where this panel is.
	Rect PlotRectAbsolute
	{
		get
		{
			var plot = PlotRect;
			plot.Position += Box.Rect.Position;
			return plot;
		}
	}

	//The curve whose viewport the grid is drawn against.
	CurveTrack GridTrack => Tracks.Count > 0 ? Tracks[^1] : null;

	public Vector2 RangeX => GridTrack?.ViewportRangeX ?? new Vector2( 0, 1 );
	public Vector2 RangeY => GridTrack?.ViewportRangeY ?? new Vector2( 0, 1 );

	/// <summary>The grid step across, which is also what Ctrl snaps a handle to.</summary>
	public float IncrementX => CurveDraw.NiceIncrement( MathF.Abs( RangeX.y - RangeX.x ), TicksX );

	public float IncrementY => CurveDraw.NiceIncrement( MathF.Abs( RangeY.y - RangeY.x ), TicksY );

	/// <summary>Puts a curve on the chart, bound to wherever it is read from and written to.</summary>
	public CurveTrack AddCurve( Func<Curve> read, Action<Curve> write )
	{
		var track = new CurveTrack( read, write )
		{
			Size = PlotRect.Size,
			HandleDistance = 50f * ScaleToScreen
		};

		track.OnChanged = () =>
		{
			OnEdited?.Invoke();
			Invalidate();
		};

		track.UpdateHandlePositions();

		Tracks.Add( track );

		Invalidate();

		return track;
	}

	/// <summary>Makes this a range editor: two curves, and the band between them.</summary>
	public void SetIsRange()
	{
		if ( Tracks.Count != 2 ) return;

		IsRange = true;

		foreach ( var track in Tracks )
		{
			track.IsPartOfRange = true;
		}

		Invalidate();
	}

	public void SetCanEditTimeRange( bool canEdit )
	{
		foreach ( var track in Tracks ) track.CanEditTimeRange = canEdit;
	}

	public void SetCanEditValueRange( bool canEdit )
	{
		foreach ( var track in Tracks ) track.CanEditValueRange = canEdit;
	}

	/// <summary>Fits every curve's view to the whole of it.</summary>
	public void FitToScreen()
	{
		foreach ( var track in Tracks )
		{
			track.FitViewportToCurve();
			track.UpdateHandlePositions();
		}

		Changed();
	}

	void Changed()
	{
		OnViewChanged?.Invoke();
		Invalidate();
	}

	//The plot needs drawing again: its labels placed on the next tick, and its vectors drawn on
	//the next frame. Nothing else redraws it, so an editor showing a still chart keeps its frame.
	void Invalidate()
	{
		plotDirty = true;
		plotPanel?.MarkRenderDirty();
	}

	public override void Tick()
	{
		base.Tick();

		SyncPlotSize();

		//Dragged from here, so the pointer is followed even once it has left.
		if ( dragMode != Drag.None )
		{
			if ( (Application.MouseButtons & (MouseButtons.Left | MouseButtons.Middle)) == 0 )
			{
				EndDrag();
			}
			else
			{
				var pointer = PointerLocal();

				if ( !pointer.AlmostEqual( dragLast ) )
				{
					ContinueDrag( pointer );
				}
			}
		}

		DropStaleReferences();
		SyncPopup();
		RenderPlot();
	}

	//Changing a range rebuilds the handles, so what was hovered or dragged may be gone.
	void DropStaleReferences()
	{
		if ( hoveredHandle is not null && hoveredTrack?.Handles.Contains( hoveredHandle ) != true )
		{
			hoveredHandle = null;
			hoveredTangent = CurveTrack.TangentSide.None;

			Invalidate();
		}

		if ( dragHandle is not null && dragTrack?.Handles.Contains( dragHandle ) != true )
		{
			EndDrag();
		}
	}

	//Hands the plot's size to every curve, and moves the handles with it.
	void SyncPlotSize()
	{
		var size = PlotRect.Size;

		if ( size.AlmostEqual( lastPlotSize ) ) return;

		lastPlotSize = size;

		var distance = 50f * ScaleToScreen;

		foreach ( var track in Tracks )
		{
			track.Size = size;
			track.HandleDistance = distance;
			track.UpdateHandlePositions();
		}

		Invalidate();
	}

	//The card follows whichever handle is under the pointer, or failing that the selected one.
	void SyncPopup()
	{
		var track = hoveredTrack;
		var handle = hoveredHandle;

		if ( handle is null )
		{
			foreach ( var candidate in Tracks )
			{
				handle = candidate.Handles.FirstOrDefault( x => x.Selected );

				if ( handle is not null )
				{
					track = candidate;
					break;
				}
			}
		}

		if ( handle is null || track is null )
		{
			popup.SetClass( "hidden", true );
			return;
		}

		var plot = PlotRect;

		popup.SetClass( "hidden", false );
		popup.UpdateFrom( track, handle, plot.Position + handle.Position, new Rect( 0, Box.Rect.Size ) );
	}

	//Places the plot panel and the axis labels when the view or the size moved.
	void RenderPlot()
	{
		var plot = PlotRect;

		var width = plot.Width.FloorToInt();
		var height = plot.Height.FloorToInt();

		if ( width < 2 || height < 2 ) return;

		var size = new Vector2Int( width, height );

		if ( !plotDirty && size == painted ) return;

		plotDirty = false;
		painted = size;

		var scale = ScaleFromScreen;

		plotPanel.Style.Left = Length.Pixels( plot.Left * scale );
		plotPanel.Style.Top = Length.Pixels( plot.Top * scale );
		plotPanel.Style.Width = Length.Pixels( width * scale );
		plotPanel.Style.Height = Length.Pixels( height * scale );

		PlaceGridLabels( new Rect( 0, 0, width, height ) );
	}

	//The plot as vectors: grid, range, band, curves and handles, stroked into the plot panel's
	//box and clipped by it. Drawn every frame the chart is on screen; there is no picture to keep.
	void DrawPlot( Vector2 origin )
	{
		var width = painted.x;
		var height = painted.y;

		if ( width < 2 || height < 2 ) return;

		canvas = new CurveDraw.Canvas( origin );

		var area = new Rect( 0, 0, width, height );

		DrawGrid( area );
		DrawHighlight( area );
		DrawRangeBand( area );

		foreach ( var track in Tracks )
		{
			DrawCurve( track, area );
		}

		foreach ( var track in Tracks )
		{
			DrawHandles( track, area );
		}
	}

	//The plot's own panel, which draws it - so the drawing is clipped to the plot as the bitmap was.
	sealed class PlotPanel : Panel
	{
		readonly CurveChartPanel chart;

		public PlotPanel( CurveChartPanel chart )
		{
			this.chart = chart;
			AddClass( "plot" );
		}

		//The chart says when the plot changes - see Invalidate - so what it drew is kept until then.
		protected override bool RetainsDrawing => true;

		public override void OnDraw() => chart.DrawPlot( Box.Rect.Position );
	}

	void DrawGrid( Rect plot )
	{
		var lineColor = Color.White.WithAlpha( 0.25f );

		{
			var rangeX = RangeX;
			var span = MathF.Abs( rangeX.y - rangeX.x );

			if ( span > 0 )
			{
				var step = CurveDraw.NiceIncrement( span, TicksX );
				var first = MathF.Ceiling( rangeX.x / step ) * step;

				for ( var v = first; v <= rangeX.y + step * 0.5f; v += step )
				{
					var t = (v - rangeX.x) / span;
					var x = plot.Left + t * plot.Width;

					if ( x < plot.Left - 0.5f || x > plot.Right + 0.5f ) continue;

					CurveDraw.Line( canvas, new Vector2( x, plot.Top ), new Vector2( x, plot.Bottom ),
						lineColor.WithAlphaMultiplied( v.AlmostEqual( 0.0f ) ? 1f : 0.3f ), 1 );
				}
			}
		}

		{
			var rangeY = RangeY;
			var span = MathF.Abs( rangeY.y - rangeY.x );

			if ( span > 0 )
			{
				var step = CurveDraw.NiceIncrement( span, TicksY );
				var first = MathF.Ceiling( rangeY.x / step ) * step;

				for ( var v = first; v <= rangeY.y + step * 0.5f; v += step )
				{
					var t = (v - rangeY.x) / span;
					var y = plot.Bottom - t * plot.Height;

					if ( y < plot.Top - 0.5f || y > plot.Bottom + 0.5f ) continue;

					CurveDraw.Line( canvas, new Vector2( plot.Left, y ), new Vector2( plot.Right, y ),
						lineColor.WithAlphaMultiplied( v.AlmostEqual( 0.0f ) ? 0.4f : 0.3f ), 1 );
				}
			}
		}
	}

	//The axis labels at the grid's ticks.
	void PlaceGridLabels( Rect plot )
	{
		var xUsed = 0;
		var yUsed = 0;

		var scale = ScaleFromScreen;
		var gutter = LeftGutter;

		{
			var rangeX = RangeX;
			var span = MathF.Abs( rangeX.y - rangeX.x );

			if ( span > 0 )
			{
				var step = CurveDraw.NiceIncrement( span, TicksX );
				var first = MathF.Ceiling( rangeX.x / step ) * step;

				for ( var v = first; v <= rangeX.y + step * 0.5f; v += step )
				{
					var t = (v - rangeX.x) / span;
					var x = plot.Left + t * plot.Width;

					if ( x < plot.Left - 0.5f || x > plot.Right + 0.5f ) continue;

					PlaceLabel( xLabels, ref xUsed, $"{v:0.######}",
						gutter + (x * scale) - 30, (plot.Bottom * scale) + 4, 60, TextAlign.Center );
				}
			}
		}

		{
			var rangeY = RangeY;
			var span = MathF.Abs( rangeY.y - rangeY.x );

			if ( span > 0 )
			{
				var step = CurveDraw.NiceIncrement( span, TicksY );
				var first = MathF.Ceiling( rangeY.x / step ) * step;

				for ( var v = first; v <= rangeY.y + step * 0.5f; v += step )
				{
					var t = (v - rangeY.x) / span;
					var y = plot.Bottom - t * plot.Height;

					if ( y < plot.Top - 0.5f || y > plot.Bottom + 0.5f ) continue;

					PlaceLabel( yLabels, ref yUsed, $"{v:0.######}",
						0, (y * scale) - 8, gutter - 6, TextAlign.Right );
				}
			}
		}

		HideSpareLabels( xLabels, xUsed );
		HideSpareLabels( yLabels, yUsed );
	}

	//An axis label, taken from the pool and put where the tick is.
	void PlaceLabel( List<Rinten.UI.Label> pool, ref int used, string text, float left, float top, float width, TextAlign align )
	{
		while ( pool.Count <= used )
		{
			var made = AddChild<Rinten.UI.Label>( "axis-label" );
			made.Tokenize = false;

			pool.Add( made );
		}

		var label = pool[used++];

		label.SetClass( "hidden", false );
		label.Style.Left = Length.Pixels( left );
		label.Style.Top = Length.Pixels( top );
		label.Style.Width = Length.Pixels( width );
		label.Style.TextAlign = align;
		label.Text = text;
	}

	static void HideSpareLabels( List<Rinten.UI.Label> pool, int used )
	{
		for ( var i = used; i < pool.Count; i++ )
		{
			pool[i].SetClass( "hidden", true );
		}
	}

	//The dashed box showing where the curve's own range sits inside the view.
	void DrawHighlight( Rect plot )
	{
		var track = GridTrack;

		if ( track is null ) return;

		var rangeX = RangeX;
		var rangeY = RangeY;

		var x1 = track.TimeRange.x.Remap( rangeX.x, rangeX.y, 0, plot.Width, false );
		var x2 = track.TimeRange.y.Remap( rangeX.x, rangeX.y, 0, plot.Width, false );
		var y1 = track.ValueRange.x.Remap( rangeY.x, rangeY.y, plot.Height, 0, false );
		var y2 = track.ValueRange.y.Remap( rangeY.x, rangeY.y, plot.Height, 0, false );

		var rect = new Rect( x1, MathF.Min( y1, y2 ), x2 - x1, MathF.Abs( y2 - y1 ) );

		if ( rect.Width <= 0 || rect.Height <= 0 ) return;

		CurveDraw.FillRect( canvas, rect, Color.White.WithAlpha( 0.05f ) );
		CurveDraw.DashedRect( canvas, rect, Color.White.WithAlpha( 0.2f ), 1 );
	}

	//The band between the two curves of a range, and the lines through it.
	void DrawRangeBand( Rect plot )
	{
		if ( !IsRange || Tracks.Count != 2 ) return;

		var range = new CurveRange( Tracks[0].GetViewportAdjustedCurve(), Tracks[1].GetViewportAdjustedCurve() );

		CurveDraw.SampleRange( range, plot, 0, scratch );
		CurveDraw.SampleRange( range, plot, 1, scratchB );
		CurveDraw.Band( canvas, scratch, scratchB, Tracks[0].CurveColor.WithAlpha( 0.2f ), scratchC );

		const float steps = 6;

		for ( var i = 0f; i <= steps; i++ )
		{
			CurveDraw.SampleRange( range, plot, (1f / steps) + i / (steps + 2f), scratch, 4.0f );
			CurveDraw.Polyline( canvas, scratch, Tracks[0].CurveColor.WithAlpha( 0.1f ), 1 );
		}
	}

	void DrawCurve( CurveTrack track, Rect plot )
	{
		if ( track.Handles.Count == 0 ) return;

		var alpha = 0.8f;

		if ( track.IsPartOfRange ) alpha *= 0.5f;
		if ( hoveredTrack != track ) alpha *= 0.5f;

		CurveDraw.SampleCurve( track.GetViewportAdjustedCurve(), plot, scratch );
		CurveDraw.Polyline( canvas, scratch, track.CurveColor.WithAlpha( alpha ), 2.0f );
	}

	void DrawHandles( CurveTrack track, Rect plot )
	{
		foreach ( var handle in track.Handles )
		{
			var at = handle.Position;

			if ( !plot.IsInside( at ) ) continue;

			var isHovered = hoveredHandle == handle;

			DrawHandleExtras( track, handle, at, plot, isHovered );

			var size = 4.0f;
			var brush = track.CurveColor;

			if ( handle.Selected )
			{
				brush = track.SelectionColor;
				size += 3;
			}

			if ( isHovered ) size += 2;

			CurveDraw.FillCircle( canvas, at, size, brush );

			if ( isHovered )
			{
				CurveDraw.StrokeCircle( canvas, at, size, track.SelectionColor, 2 );
			}
		}
	}

	//The guides and tangent grips that only show up around the handle under the pointer.
	void DrawHandleExtras( CurveTrack track, CurveTrack.Handle handle, Vector2 at, Rect plot, bool isHovered )
	{
		if ( isHovered )
		{
			CurveDraw.DottedLine( canvas,
				new Vector2( plot.Left, at.y ), new Vector2( plot.Right, at.y ),
				track.CurveColor.WithAlpha( 0.3f ), 1 );

			if ( hoveredTangent != CurveTrack.TangentSide.None )
			{
				CurveDraw.DottedCircle( canvas, at, track.HandleDistance, track.CurveColor.WithAlpha( 0.1f ), 1 );
			}
		}

		if ( !handle.HasTangents ) return;

		var lineColor = isHovered
			? track.SelectionColor.WithAlpha( 0.5f )
			: track.CurveColor.WithAlpha( 0.1f );

		DrawTangent( track, handle, CurveTrack.TangentSide.In, at, lineColor, isHovered );
		DrawTangent( track, handle, CurveTrack.TangentSide.Out, at, lineColor, isHovered );
	}

	void DrawTangent( CurveTrack track, CurveTrack.Handle handle, CurveTrack.TangentSide side, Vector2 at, Color lineColor, bool handleHovered )
	{
		var grip = at + track.GetTangentOffset( handle, side );

		CurveDraw.DottedLine( canvas, at, grip, lineColor, 1 );

		var size = 5f;
		var color = track.CurveColor.WithAlpha( 0.1f );

		if ( handleHovered ) color = track.SelectionColor;

		if ( hoveredHandle == handle && hoveredTangent == side )
		{
			color = track.SelectionColor;
			size += 2;
		}

		if ( dragMode == Drag.Tangent && dragHandle == handle && dragTangent == side )
		{
			color = track.SelectionColor;
			size += 3;
		}

		CurveDraw.StrokeRect( canvas, new Rect( grip - size * 0.5f, size ), color, 1 );
	}

	Vector2 EventLocal( MousePanelEvent e ) => ScreenPositionToPanelPosition( e.PointerPosition );

	Vector2 PointerLocal() => MousePosition;

	void OnMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe ) return;

		var local = EventLocal( mpe );

		Focus();

		if ( mpe.MouseButton == MouseButtons.Middle )
		{
			dragMode = Drag.Pan;
			dragLast = local;
			e.StopPropagation();
			return;
		}

		var plotLocal = local - PlotRect.Position;

		if ( mpe.MouseButton == MouseButtons.Right )
		{
			var (track, handle, _) = Pick( plotLocal );

			if ( handle is not null )
			{
				OpenHandleMenu( track, handle );
				e.StopPropagation();
			}

			return;
		}

		if ( mpe.MouseButton != MouseButtons.Left ) return;

		e.StopPropagation();

		{
			var (track, handle, tangent) = Pick( plotLocal );

			if ( handle is not null && tangent != CurveTrack.TangentSide.None )
			{
				Select( track, handle, alone: true );

				dragMode = Drag.Tangent;
				dragTrack = track;
				dragHandle = handle;
				dragTangent = tangent;
				dragLast = local;

				Invalidate();
				return;
			}

			if ( handle is not null )
			{
				Select( track, handle, alone: !HasCtrl && !handle.Selected );

				dragMode = Drag.Handle;
				dragTrack = track;
				dragHandle = handle;
				dragLast = local;

				Invalidate();
				return;
			}
		}

		//Nothing to grab, so a click on the curve itself puts a handle there.
		var onCurve = PickCurve( plotLocal );

		if ( onCurve is not null )
		{
			var added = onCurve.AddHandleAt( plotLocal.x );

			Select( onCurve, added, alone: true );

			dragMode = Drag.Handle;
			dragTrack = onCurve;
			dragHandle = added;
			dragLast = local;

			Invalidate();
			return;
		}

		Select( null, null, alone: true );
	}

	void OnMouseMove( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe ) return;

		if ( dragMode != Drag.None )
		{
			e.StopPropagation();
			return;
		}

		UpdateHover( EventLocal( mpe ) );
	}

	void OnMouseUp( PanelEvent e )
	{
		if ( dragMode == Drag.None ) return;

		EndDrag();

		e.StopPropagation();
	}

	void OnMouseLeave( PanelEvent e )
	{
		if ( dragMode != Drag.None ) return;

		if ( hoveredHandle is null && hoveredTrack is null ) return;

		hoveredHandle = null;
		hoveredTrack = null;
		hoveredTangent = CurveTrack.TangentSide.None;

		Invalidate();
	}

	public override void OnMouseWheel( Vector2 value )
	{
		if ( Tracks.Count == 0 || value.y.AlmostEqual( 0 ) )
		{
			base.OnMouseWheel( value );
			return;
		}

		//Wheel down is a positive y, and pushing the wheel away zooms in.
		var mult = -MathF.Sign( value.y ) * 0.1f;

		var ctrl = HasCtrl;
		var shift = HasShift;
		var alt = HasAlt;

		var both = !ctrl && !alt && !shift;

		foreach ( var track in Tracks )
		{
			if ( ctrl || both )
			{
				if ( MoveRangeWhenPanning && track.CanEditTimeRange )
				{
					var timeRange = track.TimeRange;
					var length = MathF.Abs( timeRange.x - timeRange.y );

					track.UpdateTimeRange( new Vector2(
						timeRange.x + (length * mult),
						timeRange.y - (length * mult) ), false );
				}

				var rangeX = track.ViewportRangeX;
				var lengthX = MathF.Abs( rangeX.x - rangeX.y );

				track.UpdateZoomRangeX( new Vector2(
					rangeX.x + (lengthX * mult),
					rangeX.y - (lengthX * mult) ) );
			}

			if ( shift || both )
			{
				if ( MoveRangeWhenPanning && track.CanEditValueRange )
				{
					var valueRange = track.ValueRange;
					var length = MathF.Abs( valueRange.x - valueRange.y );

					track.UpdateValueRange( new Vector2(
						valueRange.x + (length * mult),
						valueRange.y - (length * mult) ), false );
				}

				var rangeY = track.ViewportRangeY;
				var lengthY = MathF.Abs( rangeY.x - rangeY.y );

				track.UpdateZoomRangeY( new Vector2(
					rangeY.x + (lengthY * mult),
					rangeY.y - (lengthY * mult) ) );
			}

			track.UpdateHandlePositions();
		}

		Changed();
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Pressed && e.Button == "delete" )
		{
			DeleteSelected();

			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	static bool HasCtrl => Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl );
	static bool HasShift => Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Shift );
	static bool HasAlt => Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Alt );

	//The handle - or tangent grip - nearest a point of the plot, if anything is near enough.
	(CurveTrack Track, CurveTrack.Handle Handle, CurveTrack.TangentSide Tangent) Pick( Vector2 plotLocal )
	{
		var reach = GrabDistance * ScaleToScreen;

		CurveTrack bestTrack = null;
		CurveTrack.Handle bestHandle = null;
		var bestTangent = CurveTrack.TangentSide.None;
		var bestDistance = reach;

		foreach ( var track in Tracks )
		{
			foreach ( var handle in track.Handles )
			{
				var distance = handle.Position.Distance( plotLocal );

				if ( distance < bestDistance )
				{
					bestDistance = distance;
					bestTrack = track;
					bestHandle = handle;
					bestTangent = CurveTrack.TangentSide.None;
				}

				if ( !handle.HasTangents ) continue;

				PickTangent( track, handle, CurveTrack.TangentSide.In, plotLocal,
					ref bestDistance, ref bestTrack, ref bestHandle, ref bestTangent );

				PickTangent( track, handle, CurveTrack.TangentSide.Out, plotLocal,
					ref bestDistance, ref bestTrack, ref bestHandle, ref bestTangent );
			}
		}

		return (bestTrack, bestHandle, bestTangent);
	}

	static void PickTangent( CurveTrack track, CurveTrack.Handle handle, CurveTrack.TangentSide side, Vector2 plotLocal,
		ref float bestDistance, ref CurveTrack bestTrack, ref CurveTrack.Handle bestHandle, ref CurveTrack.TangentSide bestTangent )
	{
		var grip = handle.Position + track.GetTangentOffset( handle, side );
		var distance = grip.Distance( plotLocal );

		if ( distance >= bestDistance ) return;

		bestDistance = distance;
		bestTrack = track;
		bestHandle = handle;
		bestTangent = side;
	}

	//The curve passing near a point of the plot, if any does.
	CurveTrack PickCurve( Vector2 plotLocal )
	{
		var reach = CurveGrabDistance * ScaleToScreen;

		CurveTrack best = null;
		var bestDistance = reach;

		foreach ( var track in Tracks )
		{
			var distance = MathF.Abs( track.EvaluateAtLocalX( plotLocal.x ) - plotLocal.y );

			if ( distance >= bestDistance ) continue;

			bestDistance = distance;
			best = track;
		}

		return best;
	}

	void UpdateHover( Vector2 local )
	{
		var plotLocal = local - PlotRect.Position;

		var (track, handle, tangent) = Pick( plotLocal );

		track ??= PickCurve( plotLocal );

		if ( track == hoveredTrack && handle == hoveredHandle && tangent == hoveredTangent ) return;

		hoveredTrack = track;
		hoveredHandle = handle;
		hoveredTangent = tangent;

		SetClass( "over-handle", handle is not null );

		Invalidate();
	}

	void Select( CurveTrack track, CurveTrack.Handle handle, bool alone )
	{
		if ( alone )
		{
			foreach ( var other in Tracks )
			{
				foreach ( var each in other.Handles )
				{
					if ( each != handle ) each.Selected = false;
				}
			}
		}

		if ( handle is not null ) handle.Selected = true;

		Invalidate();
	}

	void ContinueDrag( Vector2 local )
	{
		var previous = dragLast;

		dragLast = local;

		switch ( dragMode )
		{
			case Drag.Pan:
				Pan( local - previous );
				break;

			case Drag.Handle:
				{
					if ( dragTrack is null || dragHandle is null ) return;

					var snapX = HasCtrl ? IncrementX : 0;
					var snapY = HasCtrl ? IncrementY : 0;

					dragTrack.MoveHandle( dragHandle, local - PlotRect.Position, snapX, snapY );

					Invalidate();
					break;
				}

			case Drag.Tangent:
				{
					if ( dragTrack is null || dragHandle is null ) return;

					var offset = (local - PlotRect.Position) - dragHandle.Position;

					dragTrack.SetTangentFrom( dragHandle, dragTangent, offset );

					Invalidate();
					break;
				}
		}
	}

	void EndDrag()
	{
		if ( dragMode == Drag.None ) return;

		dragMode = Drag.None;
		dragTrack = null;
		dragHandle = null;
		dragTangent = CurveTrack.TangentSide.None;

		Changed();
	}

	//Moves the view - and where asked, the curve's range with it.
	void Pan( Vector2 delta )
	{
		var plot = PlotRect;

		if ( plot.Width <= 0 || plot.Height <= 0 ) return;

		foreach ( var track in Tracks )
		{
			var rangeX = track.ViewportRangeX;
			var rangeY = track.ViewportRangeY;

			var deltaX = delta.x / plot.Width * MathF.Abs( rangeX.x - rangeX.y );
			var deltaY = delta.y / plot.Height * MathF.Abs( rangeY.x - rangeY.y );

			if ( MoveRangeWhenPanning )
			{
				if ( track.CanEditTimeRange )
				{
					var timeRange = track.TimeRange;
					track.UpdateTimeRange( new Vector2( timeRange.x - deltaX, timeRange.y - deltaX ), false );
				}

				if ( track.CanEditValueRange )
				{
					var valueRange = track.ValueRange;
					track.UpdateValueRange( new Vector2( valueRange.x + deltaY, valueRange.y + deltaY ), false );
				}
			}

			track.UpdateZoomRangeX( new Vector2( rangeX.x - deltaX, rangeX.y - deltaX ) );
			track.UpdateZoomRangeY( new Vector2( rangeY.x + deltaY, rangeY.y + deltaY ) );
			track.UpdateHandlePositions();
		}

		Changed();
	}

	void DeleteSelected()
	{
		var deleted = false;

		foreach ( var track in Tracks )
		{
			foreach ( var handle in track.Handles.Where( x => x.Selected ).ToArray() )
			{
				deleted |= track.Remove( handle );
			}
		}

		if ( !deleted ) return;

		hoveredHandle = null;

		Changed();
	}

	void OpenHandleMenu( CurveTrack track, CurveTrack.Handle handle )
	{
		Select( track, handle, alone: true );

		var menu = new Menu();

		foreach ( var (value, info) in DisplayInfo.ForEnumValues<Curve.HandleMode>() )
		{
			var option = menu.AddOption( info.Name, info.Icon, () =>
			{
				track.SetHandleMode( handle, value );
				Invalidate();
			} );

			option.Enabled = handle.Frame.Mode != value;
		}

		menu.AddSeparator();

		menu.AddOption( "Delete", "delete", () =>
		{
			if ( !track.Remove( handle ) ) return;

			hoveredHandle = null;
			Changed();
		} );

		menu.OpenAtCursor( this );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

	}
}
