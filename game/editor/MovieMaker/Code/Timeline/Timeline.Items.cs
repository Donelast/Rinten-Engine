using Rinten.MovieMaker;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

//What is on the timeline, what is picked out, and what is under the pointer. Everything hit-testable lives on the one moving layer in
//scene coordinates, so which item a point is on is a question about rectangles and nothing has to walk a tree of parents to answer it.
partial class Timeline
{
	private readonly List<TimelineItem> _items = new();
	private readonly List<TimelineItem> _selection = new();

	private TimelineItem? _hovered;

	/// <summary>Everything on the timeline that can be hit or snapped to.</summary>
	public IEnumerable<TimelineItem> Items => _items;

	/// <summary>Everything picked out.</summary>
	public IEnumerable<TimelineItem> SelectedItems => _selection;

	/// <summary>Puts an item on the timeline.</summary>
	public T AddItem<T>( T item )
		where T : TimelineItem
	{
		if ( _items.Contains( item ) ) return item;

		_items.Add( item );

		if ( !item.Parent.IsValid() )
		{
			_pan.AddChild( item );
		}

		return item;
	}

	/// <summary>Takes an item off the timeline and destroys it.</summary>
	public void Remove( TimelineItem item )
	{
		Forget( item );

		item.Delete();
	}

	//Takes an item off the timeline without destroying it.
	internal void Forget( TimelineItem item )
	{
		_items.Remove( item );
		_selection.Remove( item );

		if ( _hovered == item ) _hovered = null;
	}

	public void DeleteAllItems()
	{
		foreach ( var item in _items.ToArray() )
		{
			item.Delete();
		}

		_items.Clear();
		_selection.Clear();

		_hovered = null;
	}

	/// <summary>Picks an item out, or takes it out of the selection.</summary>
	public void SetSelected( TimelineItem item, bool selected )
	{
		if ( selected )
		{
			if ( _selection.Contains( item ) ) return;

			_selection.Add( item );
			item.ApplySelected( true );
		}
		else
		{
			if ( !_selection.Remove( item ) ) return;

			item.ApplySelected( false );
		}

		OnSelectionChanged?.Invoke();
	}

	public void DeselectAll()
	{
		Session.TrackList.DeselectAll();

		if ( _selection.Count == 0 ) return;

		foreach ( var item in _selection.ToArray() )
		{
			if ( !item.IsValid() ) continue;

			item.ApplySelected( false );
		}

		_selection.Clear();

		OnSelectionChanged?.Invoke();
	}

	//How far past the edge of the window an item is kept, so it does not flap while panning.
	private const float CullMargin = 256f;

	//Removes off-screen items from layout to save cost; skipped while dragging
	private void CullItems()
	{
		if ( IsDragging ) return;

		var visible = VisibleRect.Grow( CullMargin );

		foreach ( var item in _items )
		{
			if ( !item.IsValid() ) continue;

			var rect = item.SceneRect;

			var onScreen = rect.Right >= visible.Left && rect.Left <= visible.Right
				&& rect.Bottom >= visible.Top && rect.Top <= visible.Bottom;

			item.SetCulled( !onScreen );
		}
	}

	/// <summary>Whatever selectable item is at a point of the timeline, frontmost first.</summary>
	public TimelineItem? GetItemAt( Vector2 scenePos )
	{
		TimelineItem? best = null;

		foreach ( var item in _items )
		{
			if ( !item.IsValid() ) continue;
			if ( !item.Selectable ) continue;
			if ( item.IsCulled ) continue;
			if ( !item.IsVisibleSelf ) continue;
			if ( !item.ContainsScenePoint( scenePos ) ) continue;

			if ( best is null || item.ZIndex >= best.ZIndex )
			{
				best = item;
			}
		}

		return best;
	}

	/// <summary>Whichever track row a point of the timeline is level with.</summary>
	public TimelineTrack? GetTrackAt( Vector2 scenePos ) =>
		_tracks.FirstOrDefault( x => x.IsValid() && x.SceneRect.IsInside( scenePos ) );

	private TimelineTrack? _hoveredTrack;

	private void UpdateHovered( Vector2 scenePos )
	{
		var under = GetItemAt( scenePos );

		if ( under != _hovered )
		{
			if ( _hovered.IsValid() ) _hovered.Hovered = false;

			_hovered = under;

			if ( _hovered.IsValid() ) _hovered.Hovered = true;
		}

		var track = GetTrackAt( scenePos );

		if ( track == _hoveredTrack ) return;

		if ( _hoveredTrack.IsValid() ) _hoveredTrack.SetHovered( false );

		_hoveredTrack = track;

		if ( _hoveredTrack.IsValid() ) _hoveredTrack.SetHovered( true );
	}

	public void UpdateSnapTargets( IEnumerable<(MovieTime Time, Rect SceneRect)> targets )
	{
		_snapTargets.Update( targets );
	}

	//Everything that other items can snap to, items placed inside items included.
	private IEnumerable<ISnapSource> SnapSources
	{
		get
		{
			foreach ( var item in _items )
			{
				if ( !item.IsValid() ) continue;

				if ( item is ISnapSource source ) yield return source;

				foreach ( var child in item.ChildItems )
				{
					if ( child is ISnapSource childSource ) yield return childSource;
				}
			}
		}
	}
}
