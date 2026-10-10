using Rinten;
using Rinten.MovieMaker;
using Rinten.UI;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	//One of the parts of the stretch of time being worked on: the peak, one of the two fades, or a grip on a boundary between them.
	private abstract class TimeSelectionItem : TimelineItem, IMovieDraggable, IMovieContextMenu
	{
		//Capture time selection before being dragged so we can revert etc.
		protected TimeSelection? OriginalSelection { get; private set; }
		protected IModificationOptions? OriginalModificationOptions { get; private set; }

		public MotionEditMode EditMode { get; }

		protected TimeSelectionItem( MotionEditMode editMode )
			: base( editMode.Timeline )
		{
			EditMode = editMode;

			AddClass( "time-selection" );

			Selectable = true;
		}

		public abstract void UpdatePosition( TimeSelection value, Rect viewRect );

		/// <summary>The ends are for the scrub bars, not for this.</summary>
		public override bool ContainsScenePoint( Vector2 scenePos ) =>
			SceneRect.Shrink( 0f, 24f, 0f, 24f ).IsInside( scenePos );

		public abstract MovieTimeRange TimeRange { get; }

		public virtual (MovieTime? Min, MovieTime? Max) DragLimits => (-TimeRange.Start, null);

		public virtual string DragCursor => MovieCursor.Grab;

		public virtual IEnumerable<MovieTime> SnapSources => [TimeRange.Start, TimeRange.End];

		void IMovieDraggable.StartDrag()
		{
			OriginalSelection = EditMode.TimeSelection;
			OriginalModificationOptions = EditMode.Modification?.Options;

			OnStartDrag();
		}

		protected virtual void OnStartDrag() { }

		void IMovieDraggable.Drag( MovieTime delta ) => OnDrag( delta );

		protected virtual void OnDrag( MovieTime delta ) { }

		void IMovieDraggable.EndDrag()
		{
			try
			{
				OnEndDrag();
			}
			finally
			{
				OriginalSelection = null;
				OriginalModificationOptions = null;
			}
		}

		protected virtual void OnEndDrag() { }

		bool IMovieDraggable.SnapFilter( ISnapSource source ) => OnSnapFilter( source );

		protected virtual bool OnSnapFilter( ISnapSource source ) => source is not TimeSelectionItem;

		public void ShowContextMenu( ContextMenuEvent ev )
		{
			if ( EditMode.TimeSelection is not { } selection )
			{
				return;
			}

			ev.Title = selection.TotalTimeRange.ToString();
			ev.Accepted = true;

			EditMode.AddTimeSelectionContextMenu( ev, selection );

			OnShowContextMenu( ev );

			EditMode.AddClipboardContextMenu( ev, selection.TotalTimeRange );
		}

		protected virtual void OnShowContextMenu( ContextMenuEvent ev )
		{

		}
	}

	//Inner region of the timeline selection. Dragging it moves the whole selection left / right.
	private sealed class TimeSelectionPeakItem : TimeSelectionItem
	{
		private readonly IconPanel _actionIcon;

		public override MovieTimeRange TimeRange => EditMode.TimeSelection?.PeakTimeRange ?? default;

		public override (MovieTime? Min, MovieTime? Max) DragLimits => (-EditMode.TimeSelection?.TotalStart, null);

		public override IEnumerable<MovieTime> SnapSources
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection ) return [];

				return [selection.TotalStart, selection.PeakStart, selection.PeakEnd, selection.TotalEnd];
			}
		}

		public TimeSelectionPeakItem( MotionEditMode editMode )
			: base( editMode )
		{
			AddClass( "peak" );

			ZIndex = 10000;

			_actionIcon = AddChild<IconPanel>( "action" );
		}

		protected override void OnDrag( MovieTime delta )
		{
			if ( EditMode.TimeSelection is not { } current ) return;

			if ( EditMode.Modification?.Options is ITranslatableOptions translatable )
			{
				EditMode.Modification.Options = translatable.WithOffset( translatable.Offset + delta );
			}

			EditMode.TimeSelection = current + delta;
		}

		public override void UpdatePosition( TimeSelection value, Rect viewRect )
		{
			var timeRange = value.PeakTimeRange;

			SetSceneRect( new Rect(
				Timeline.TimeToPixels( timeRange.Start ),
				viewRect.Top - 16f,
				Timeline.TimeToPixels( timeRange.Duration ),
				viewRect.Height + 32f ) );

			var color = EditMode.SelectionColor;

			if ( Hovered ) color = color.Lighten( 0.1f );

			Style.BackgroundColor = color;

			UpdateActionIcon();
		}

		//The last thing that was done, flashed over the selection and then gone.
		private void UpdateActionIcon()
		{
			if ( EditMode.LastActionIcon is not { } icon || EditMode.SinceLastAction >= 1f )
			{
				_actionIcon.SetClass( "hidden", true );
				return;
			}

			var t = 1f - EditMode.SinceLastAction;

			_actionIcon.SetClass( "hidden", false );
			_actionIcon.Text = icon;
			_actionIcon.Style.Opacity = t * t * t * t;
		}
	}

	private enum FadeKind
	{
		FadeIn,
		FadeOut
	}

	//Fade in / out region of the timeline selection. Dragging it moves the fade left / right. If the selection has a zero-width peak (it
	//fades out right after fading in), then you can move the whole selection by starting a drag in the direction of the other fade item.
	private sealed class TimeSelectionFadeItem : TimeSelectionItem
	{
		private readonly FadeShapePanel _shape;

		private bool? _moveWholeSelection;

		public FadeKind Kind { get; }

		public override MovieTimeRange TimeRange
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection )
				{
					return default;
				}

				if ( _moveWholeSelection is not false )
				{
					return selection.TotalTimeRange;
				}

				return Kind == FadeKind.FadeIn
					? selection.FadeInTimeRange
					: selection.FadeOutTimeRange;
			}
		}

		public InterpolationMode? Interpolation
		{
			get => Kind == FadeKind.FadeIn
				? EditMode.TimeSelection?.FadeIn.Interpolation
				: EditMode.TimeSelection?.FadeOut.Interpolation;

			set
			{
				if ( EditMode.TimeSelection is not { } selection ) return;
				if ( value is not { } mode ) return;

				EditMode.TimeSelection = Kind == FadeKind.FadeIn
					? selection with { FadeIn = selection.FadeIn with { Interpolation = mode } }
					: selection with { FadeOut = selection.FadeOut with { Interpolation = mode } };
			}
		}

		public override (MovieTime? Min, MovieTime? Max) DragLimits
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection )
				{
					return default;
				}

				if ( _moveWholeSelection is not false )
				{
					return (-selection.TotalStart, null);
				}

				return Kind == FadeKind.FadeIn
					? (-selection.TotalStart, selection.PeakTimeRange.Duration)
					: (-selection.PeakTimeRange.Duration, null);
			}
		}

		public override IEnumerable<MovieTime> SnapSources
		{
			get
			{
				if ( EditMode.TimeSelection is not { } selection )
				{
					return [];
				}

				return _moveWholeSelection is not false
					? [selection.TotalStart, selection.PeakStart, selection.TotalEnd]
					: base.SnapSources;
			}
		}

		public TimeSelectionFadeItem( MotionEditMode editMode, FadeKind kind )
			: base( editMode )
		{
			Kind = kind;

			AddClass( "fade" );

			ZIndex = 10001;

			_shape = AddChild( new FadeShapePanel( kind == FadeKind.FadeOut ) );
		}

		public override void UpdatePosition( TimeSelection value, Rect viewRect )
		{
			if ( EditMode.TimeSelection is not { } selection )
			{
				SetClass( "hidden", true );
				return;
			}

			SetClass( "hidden", false );

			var timeRange = Kind == FadeKind.FadeIn
				? selection.FadeInTimeRange
				: selection.FadeOutTimeRange;

			var width = Timeline.TimeToPixels( timeRange.Duration );

			//The fade in hangs off the left of its end, the fade out off the right of its start.
			var left = Kind == FadeKind.FadeIn
				? Timeline.TimeToPixels( timeRange.End ) - width
				: Timeline.TimeToPixels( timeRange.Start );

			SetSceneRect( new Rect( left, viewRect.Top, width, viewRect.Height ) );

			var color = EditMode.SelectionColor;

			if ( Hovered ) color = color.Lighten( 0.1f );

			_shape.Set( Interpolation, color );
		}

		protected override void OnStartDrag()
		{
			if ( OriginalSelection is not { } selection ) return;

			//We can't move the whole selection if there's a peak range between the fade in / fade out.

			_moveWholeSelection = selection.PeakTimeRange.IsEmpty ? null : false;
		}

		protected override void OnDrag( MovieTime delta )
		{
			if ( EditMode.TimeSelection is not { } selection ) return;

			//We want to move the whole time selection if the fade in and fade out are adjacent,
			//and we start trying to "push" the fade in into the fade out (or vice versa).

			_moveWholeSelection ??= Kind == FadeKind.FadeIn && delta.IsPositive || Kind == FadeKind.FadeOut && delta.IsNegative;

			if ( _moveWholeSelection is true )
			{
				if ( EditMode.Modification?.Options is ITranslatableOptions translatable )
				{
					EditMode.Modification.Options = translatable.WithOffset( translatable.Offset + delta );
				}

				EditMode.TimeSelection = selection + delta;
			}
			else if ( Kind == FadeKind.FadeIn )
			{
				EditMode.TimeSelection = selection.WithTimes( totalStart: selection.TotalStart + delta, peakStart: selection.PeakStart + delta );
			}
			else
			{
				EditMode.TimeSelection = selection.WithTimes( peakEnd: selection.PeakEnd + delta, totalEnd: selection.TotalEnd + delta );
			}
		}
	}

	//Handle between selection parts (fade in/peak/fade out); zero-width parts pick by drag direction
	private sealed class TimeSelectionHandleItem : TimeSelectionItem, ISnapSource
	{
		private const float HandleWidth = 8f;

		private enum Index
		{
			TotalStart,
			PeakStart,
			PeakEnd,
			TotalEnd
		}

		private readonly Panel _line;

		private Index _minIndex;
		private Index _maxIndex;

		private MovieTime? _dragTime;

		public override string DragCursor => MovieCursor.ResizeHorizontal;

		public TimeSelectionHandleItem( MotionEditMode editMode )
			: base( editMode )
		{
			AddClass( "handle" );

			ZIndex = 10002;

			_line = AddChild<Panel>( "line" );
		}

		public override MovieTimeRange TimeRange
		{
			get
			{
				if ( EditMode.TimeSelection is not { } current )
				{
					return default;
				}

				return _dragTime ?? GetTime( current, GetIndexRange().Min );
			}
		}

		public override (MovieTime? Min, MovieTime? Max) DragLimits
		{
			get
			{
				if ( OriginalSelection is not { } original || EditMode.TimeSelection is not { } current )
				{
					return base.DragLimits;
				}

				//Limit dragging to neighbouring handles

				var curTime = GetTime( current, _minIndex );
				var minTime = GetTime( original, _minIndex - 1 );
				var maxTime = GetTime( original, _maxIndex + 1 );

				return (minTime - curTime, maxTime - curTime);
			}
		}

		protected override void OnStartDrag()
		{
			(_minIndex, _maxIndex) = GetIndexRange();

			if ( OriginalSelection is not { } original ) return;

			_dragTime = GetTime( original, _minIndex );
		}

		protected override void OnDrag( MovieTime delta )
		{
			if ( OriginalSelection is not { } original ) return;
			if ( EditMode.TimeSelection is null ) return;
			if ( _dragTime is not { } time ) return;

			time += delta;

			_dragTime = time;

			//If it's ambiguous which handle we are, pick a side based on which direction we're dragged

			if ( delta.IsNegative ) _maxIndex = _minIndex;
			if ( delta.IsPositive ) _minIndex = _maxIndex;

			EditMode.TimeSelection = SetTime( original, _minIndex, time );
		}

		protected override void OnEndDrag()
		{
			_dragTime = null;
		}

		private static MovieTime GetTime( TimeSelection value, Index index ) => index switch
		{
			Index.TotalStart => value.TotalStart,
			Index.PeakStart => value.PeakStart,
			Index.PeakEnd => value.PeakEnd,
			Index.TotalEnd => value.TotalEnd,
			< 0 => MovieTime.Zero,
			> Index.TotalEnd => MovieTime.MaxValue
		};

		private static TimeSelection SetTime( TimeSelection value, Index index, MovieTime time ) => index switch
		{
			Index.TotalStart => value.WithTimes( totalStart: time ),
			Index.PeakStart => value.WithTimes( peakStart: time ),
			Index.PeakEnd => value.WithTimes( peakEnd: time ),
			Index.TotalEnd => value.WithTimes( totalEnd: time ),
			_ => value
		};

		public override void UpdatePosition( TimeSelection value, Rect viewRect )
		{
			var time = GetTime( value, GetIndexRange().Min );
			var margin = _maxIndex == Index.TotalStart || _minIndex == Index.TotalEnd
				? ScrubBarPanel.BarHeight
				: 0f;

			SetSceneRect( new Rect(
				Timeline.TimeToPixels( time ) - HandleWidth * 0.5f,
				viewRect.Top + margin,
				HandleWidth,
				viewRect.Height - margin * 2f ) );

			_line.Style.BackgroundColor = Hovered
				? EditMode.SelectionColor.Lighten( 0.1f ).WithAlpha( 1f )
				: Color.Transparent;
		}

		//Get the possible range of handles this could be, if the time selection has overlapping control points.
		private (Index Min, Index Max) GetIndexRange()
		{
			if ( EditMode.TimeSelection is not { } value ) return default;

			var handles = EditMode.Timeline.Items.OfType<TimeSelectionHandleItem>()
				.OrderBy( x => x.SceneRect.Left );

			var index = Index.TotalStart + handles.TakeWhile( x => x != this ).Count();

			var minIndex = index;
			var maxIndex = index;

			var time = GetTime( value, index );

			while ( minIndex > Index.TotalStart && GetTime( value, minIndex - 1 ) == time )
			{
				minIndex -= 1;
			}

			while ( maxIndex < Index.TotalEnd && GetTime( value, maxIndex + 1 ) == time )
			{
				maxIndex += 1;
			}

			return (minIndex, maxIndex);
		}

		public IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary ) => [TimeRange.Start];
	}

	//The curved wedge a fade is drawn as, mirrored about the middle. A curve under a gradient, so it is painted. Only when
	//the shape or the colour actually changes - dragging one end of the selection repaints one fade, not the whole timeline.
	private sealed class FadeShapePanel : BitmapPanel
	{
		private const int Steps = 32;

		private readonly bool _flipX;

		private InterpolationMode? _interpolation;
		private Color _color = Color.White;

		public FadeShapePanel( bool flipX )
		{
			_flipX = flipX;

			AddClass( "fade-shape" );
		}

		public void Set( InterpolationMode? interpolation, Color color )
		{
			if ( _interpolation == interpolation && _color == color ) return;

			_interpolation = interpolation;
			_color = color;

			MarkDirty();
		}

		protected override Vector2Int BitmapSize => _interpolation is null
			? default
			: new Vector2Int( Math.Max( 1, Box.Rect.Width.FloorToInt() ), Math.Max( 1, Box.Rect.Height.FloorToInt() ) );

		protected override void OnDrawBitmap( Bitmap bitmap )
		{
			if ( _interpolation is not { } interpolation ) return;

			var width = bitmap.Width;
			var height = bitmap.Height;
			var curveHeight = ScrubBarPanel.BarHeight;

			var x0 = _flipX ? width : 0f;
			var x1 = _flipX ? 0f : width;

			var points = new List<Vector2>();

			AddPoints( points, interpolation, new Vector2( x0, curveHeight ), new Vector2( x1, 0f ), false );
			AddPoints( points, interpolation, new Vector2( x1, height ), new Vector2( x0, height - curveHeight ), true );

			bitmap.SetAntialias( true );
			bitmap.SetLinearGradient( new Vector2( x0, 0f ), new Vector2( x1, 0f ), new Gradient(
				new Gradient.ColorFrame( 0f, _color.WithAlpha( 0.02f ) ),
				new Gradient.ColorFrame( 1f, _color ) ) );

			bitmap.DrawPolygon( [.. points] );

			points.Add( points[0] );

			bitmap.SetPen( _color.WithAlpha( 0.5f ), 0.5f );
			bitmap.DrawLines( [.. points] );
		}

		private static void AddPoints( List<Vector2> points, InterpolationMode interpolation, Vector2 start, Vector2 end, bool flip )
		{
			var width = Math.Abs( end.x - start.x );
			var pointCount = (int)Math.Clamp( width / 4f, 2f, Steps );

			for ( var i = 0; i <= pointCount; ++i )
			{
				var t = (float)i / pointCount;
				var v = flip ? 1f - interpolation.Apply( 1f - t ) : interpolation.Apply( t );

				points.Add( start + new Vector2( t * (end.x - start.x), v * (end.y - start.y) ) );
			}
		}
	}
}
