using Rinten.MovieMaker;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>What a cursor looks like over something on the timeline, in CSS names.</summary>
public static class MovieCursor
{
	public const string Default = "default";
	public const string Grab = "pointer";
	public const string ResizeHorizontal = "e-resize";
	public const string Text = "text";
}

public interface IMovieItem
{
	bool MultiSelectable => false;
	bool OverridesMouseEvents => false;

	MovieTimeRange TimeRange { get; }

	/// <summary>Move the playhead to this time when this item is selected.</summary>
	MovieTime? SelectionTime => TimeRange.Duration.IsZero ? TimeRange.Start : null;

	/// <summary>Called when this item is clicked twice in quick succession.</summary>
	void DoubleClick() { }

	/// <summary>Called when this item is selected individually (not box selected or multi-selected with ctrl).</summary>
	void SingleSelected() { }
}

public interface ITrackItem : IMovieItem
{
	TimelineTrack Track { get; }
}

public interface IMovieDraggable : IMovieItem
{
	/// <summary>Times on this object that can snap to other objects while dragging.</summary>
	IEnumerable<MovieTime> SnapSources => [TimeRange.Start, TimeRange.End];

	string DragCursor => MovieCursor.Grab;

	/// <summary>How far this item can be dragged in either direction before hitting a limit.</summary>
	(MovieTime? Min, MovieTime? Max) DragLimits => (-TimeRange.Start, null);

	void StartDrag() { }
	void Drag( MovieTime delta );
	void EndDrag() { }

	bool SnapFilter( ISnapSource source ) => source != this;
}

public enum BlockEdge
{
	Start,
	End
}

public interface IMovieResizable : IMovieItem
{
	/// <summary>Full start / end limits when resizing. If null, can be resized without limit.</summary>
	(MovieTime? Min, MovieTime? Max) ResizeLimits => (MovieTime.Zero, null);

	string ResizeCursor => MovieCursor.ResizeHorizontal;

	void StartResize( BlockEdge edge ) { }
	void Resize( BlockEdge edge, MovieTime delta );
	void EndResize() { }
}

public interface IMovieContextMenu : IMovieItem
{
	void ShowContextMenu( EditMode.ContextMenuEvent ev );
}

public readonly record struct SnapTarget( MovieTime Time, int Priority = 0, bool Show = true )
{
	public static implicit operator SnapTarget( MovieTime time ) => new( time );
}

/// <summary>Implemented by things on a <see cref="Timeline"/> that others can snap to.</summary>
public interface ISnapSource
{
	/// <summary>Only snap if the cursor is within these bounds.</summary>
	Rect SceneSnapBounds
	{
		get
		{
			//Everything else that implements this is an item on the timeline.

			return this is TimelineItem item
				? item.SceneRect
				: throw new NotImplementedException();
		}
	}

	/// <summary>Absolute times that other objects can snap to.</summary>
	IEnumerable<SnapTarget> GetSnapTargets( MovieTime sourceTime, bool isPrimary );
}

public delegate bool SnapFilter( ISnapSource source );

public sealed record SnapOptions(
	SnapFilter? Filter = null,
	MovieTime? Min = null,
	MovieTime? Max = null,
	params MovieTime[] SnapOffsets )
{
	public SnapOptions( params MovieTime[] snapOffsets )
		: this( null, null, null, snapOffsets )
	{

	}
}

partial class Timeline
{
	private readonly List<IMovieDraggable> _draggedItems = new();
	private readonly List<(IMovieResizable Item, BlockEdge Edge)> _resizedItems = new();
	private MovieTime _lastDragTime;
	private SnapOptions? _dragSnapOptions;
	private IHistoryScope? _dragScope;

	public bool IsDragging => _draggedItems.Count > 0 || _resizedItems.Count > 0;

	//Which end of a block a point is near enough to grab, if either.
	private static BlockEdge? GetBlockEdge( Vector2 scenePos, TimelineItem item )
	{
		var rect = item.SceneRect;

		var leftMax = Math.Min( rect.Left + 8f, rect.Center.x );
		var rightMin = Math.Max( rect.Right - 8f, rect.Center.x );

		if ( scenePos.x < leftMax )
		{
			return BlockEdge.Start;
		}

		if ( scenePos.x > rightMin )
		{
			return BlockEdge.End;
		}

		return null;
	}

	private static void UpdateCursor( Vector2 scenePos, TimelineItem item )
	{
		if ( item is IMovieDraggable draggable )
		{
			item.Style.Cursor = draggable.DragCursor;
		}

		if ( item is IMovieResizable resizable && GetBlockEdge( scenePos, item ) is not null )
		{
			item.Style.Cursor = resizable.ResizeCursor;
		}
	}

	private bool StartDragging( Vector2 scenePos, TimelineItem item )
	{
		if ( item is not IMovieDraggable draggable )
		{
			return false;
		}

		if ( item.Selected )
		{
			return StartDragging( scenePos, SelectedItems.OfType<IMovieDraggable>() );
		}

		DeselectAll();
		SetSelected( item, true );

		return StartDragging( scenePos, [draggable] );
	}

	/// <summary>Drag these items from here, as though they had been picked up with the mouse. An edit mode that has just made them - see KeyframeEditMode's click-drag - passes the undo scope it made them in, so the drag lands in the same history entry.</summary>
	public bool StartDragging( Vector2 scenePos, IEnumerable<IMovieDraggable> items, IHistoryScope? undoScope = null )
	{
		_dragScope = undoScope;
		_lastDragTime = ScenePositionToTime( scenePos );

		_draggedItems.Clear();
		_resizedItems.Clear();

		_draggedItems.AddRange( items );
		_dragSnapOptions = new SnapOptions( Filter: x => _draggedItems.All( item => item.SnapFilter( x ) ) );

		foreach ( var dragged in _draggedItems )
		{
			dragged.StartDrag();
		}

		return _draggedItems.Count > 0;
	}

	private bool StartResizing( Vector2 scenePos, TimelineItem item )
	{
		if ( item is not IMovieResizable resizable )
		{
			return false;
		}

		if ( GetBlockEdge( scenePos, item ) is not { } edge )
		{
			return false;
		}

		if ( !item.Selected )
		{
			DeselectAll();
			SetSelected( item, true );
		}

		_lastDragTime = edge == BlockEdge.Start ? resizable.TimeRange.Start : resizable.TimeRange.End;

		_draggedItems.Clear();
		_resizedItems.Clear();

		_resizedItems.AddRange( SelectedItems.OfType<IMovieResizable>()
			.Where( x => x.TimeRange.Start == _lastDragTime || x.TimeRange.End == _lastDragTime )
			.Select( x => (x, x.TimeRange.Start == _lastDragTime ? BlockEdge.Start : BlockEdge.End) ) );

		_dragSnapOptions = new SnapOptions();

		foreach ( var itemEdge in _resizedItems )
		{
			itemEdge.Item.StartResize( itemEdge.Edge );
		}

		return _resizedItems.Count > 0;
	}

	private static (MovieTime? Min, MovieTime? Max) GetDragLimits( IMovieResizable item, BlockEdge edge )
	{
		return edge == BlockEdge.Start
			? (item.ResizeLimits.Min, item.TimeRange.End)
			: (item.TimeRange.Start, item.ResizeLimits.Max);
	}

	private static (MovieTime? Min, MovieTime? Max) GetDragLimits( IEnumerable<(MovieTime? Min, MovieTime? Max)> itemLimits )
	{
		var minDragTime = (MovieTime?)null;
		var maxDragTime = (MovieTime?)null;

		foreach ( var limits in itemLimits )
		{
			if ( limits.Min is { } min )
			{
				minDragTime = MovieTime.Max( minDragTime ?? min, min );
			}

			if ( limits.Max is { } max )
			{
				maxDragTime = MovieTime.Min( maxDragTime ?? max, max );
			}
		}

		return (minDragTime, maxDragTime);
	}

	private void DragTo( Vector2 scenePos )
	{
		var visibleRect = VisibleRect;

		scenePos.x = Math.Clamp( scenePos.x, visibleRect.Left, visibleRect.Right );

		var isResizing = _resizedItems.Count > 0;
		var itemLimits = isResizing
			? _resizedItems.Select( x => GetDragLimits( x.Item, x.Edge ) )
			: _draggedItems.Select( x => (x.DragLimits.Min + _lastDragTime, x.DragLimits.Max + _lastDragTime) );

		var (minDragTime, maxDragTime) = GetDragLimits( itemLimits );

		var snapOffsets = isResizing
			? _dragSnapOptions!.SnapOffsets
			: _draggedItems
				.SelectMany( x => x.SnapSources )
				.Select( x => x - _lastDragTime )
				.Distinct()
				.Order()
				.ToArray();

		var time = ScenePositionToTime( scenePos, _dragSnapOptions! with { Min = minDragTime, Max = maxDragTime, SnapOffsets = snapOffsets } );
		var delta = time - _lastDragTime;

		if ( delta.IsZero ) return;

		_lastDragTime = time;
		_dragScope ??= Session.History.Push( isResizing ? "Resize Selection" : "Drag Selection" );

		Session.PreviewTime = time + snapOffsets.DefaultIfEmpty().MinBy( x => x.Absolute );

		if ( isResizing )
		{
			foreach ( var (item, edge) in _resizedItems )
			{
				item.Resize( edge, delta );
			}
		}
		else
		{
			foreach ( var item in _draggedItems )
			{
				item.Drag( delta );
			}

			Session.EditMode?.DragItems( _draggedItems, delta );
		}

		Session.RefreshNextFrame();

		_dragScope?.PostChange();
	}

	private void StopDragging()
	{
		var isResizing = _resizedItems.Count > 0;

		if ( isResizing )
		{
			foreach ( var (item, _) in _resizedItems )
			{
				item.EndResize();
			}
		}
		else
		{
			foreach ( var item in _draggedItems )
			{
				item.EndDrag();
			}

			Session.EditMode?.EndDragItems( _draggedItems );
		}

		_dragScope?.Dispose();
		_dragScope = null;

		_draggedItems.Clear();
		_resizedItems.Clear();
	}
}
