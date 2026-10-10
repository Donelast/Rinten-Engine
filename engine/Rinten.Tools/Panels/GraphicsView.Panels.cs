using Rinten;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>A view onto a scene of items.</summary>
public partial class GraphicsView
{
	private readonly List<GraphicsItem> items = new();
	private readonly List<GraphicsItem> selected = new();

	private Vector2 centre;

	private GraphicsItem pressed;
	private GraphicsItem dragging;
	private GraphicsItem hovered;

	private Vector2 pressedAt;

	//Drag measured from the press, not per event, so grid snapping doesn't swallow slow movement
	private Vector2 dragFrom;

	private readonly List<(GraphicsItem Item, Vector2 Start)> dragStarts = new();
	private bool panning;

	internal IEnumerable<GraphicsItem> ItemsManaged => items;
	internal IEnumerable<GraphicsItem> SelectedManaged => selected;

	internal Vector2 CentreManaged
	{
		get => centre;
		set { centre = value; Update(); }
	}

	internal Vector2 ScaleManaged
	{
		get => _scale;
		set
		{
			_scale = new Vector2(
				Math.Clamp( value.x, MinZoom, MaxZoom ),
				Math.Clamp( value.y, MinZoom, MaxZoom ) );

			Update();
		}
	}

	internal void AddManaged( GraphicsItem item )
	{
		if ( item is null || items.Contains( item ) ) return;

		items.Add( item );

		item.SetViewManaged( this );

		Update();
	}

	internal void RemoveManaged( GraphicsItem item )
	{
		if ( item is null ) return;

		items.Remove( item );
		selected.Remove( item );

		if ( pressed == item ) pressed = null;
		if ( dragging == item ) dragging = null;
		if ( hovered == item ) hovered = null;

		Update();
	}

	internal void ClearManaged()
	{
		foreach ( var item in items.ToArray() ) item.Destroy();

		items.Clear();
		selected.Clear();

		Update();
	}

	//Where a point of the view is in the scene.
	internal Vector2 ToSceneManaged( Vector2 view )
	{
		return ((view - (Size * 0.5f)) / _scale) + centre;
	}

	//And the other way.
	internal Vector2 FromSceneManaged( Vector2 scene )
	{
		return ((scene - centre) * _scale) + (Size * 0.5f);
	}

	//The part of the scene this view is showing.
	internal Rect SceneRectManaged
	{
		get
		{
			var topLeft = ToSceneManaged( 0 );
			var bottomRight = ToSceneManaged( Size );

			return new Rect( topLeft, bottomRight - topLeft );
		}
	}

	//Look at this part of the scene, all of it, and no closer than it takes.
	internal void FitInViewManaged( Rect rect )
	{
		if ( rect.Width <= 0 || rect.Height <= 0 ) return;

		var wanted = MathF.Min( Size.x / rect.Width, Size.y / rect.Height );

		ScaleManaged = wanted;
		CentreManaged = rect.Center;
	}

	//Closer or further out, about a point of the view.
	internal void ZoomManaged( float adjust, Vector2 viewPosition )
	{
		var before = ToSceneManaged( viewPosition );

		ScaleManaged = _scale * adjust;

		centre += before - ToSceneManaged( viewPosition );

		Update();
	}

	//The picture behind the scene, tiled as the graphs want it.
	internal Pixmap BackgroundManaged { get; set; }

	//Everything inside a rectangle of the scene, selected.
	internal void SelectInRectManaged( Rect rect )
	{
		foreach ( var item in items.ToArray() )
		{
			if ( !item.IsValid ) continue;

			var where = item.SceneBoundsManaged;

			var hit = where.Right >= rect.Left && where.Left <= rect.Right &&
				where.Bottom >= rect.Top && where.Top <= rect.Bottom;

			if ( hit && item.Selectable ) SelectManaged( item, alone: false );
			else if ( !hit ) DeselectManaged( item );
		}
	}

	//What the view is showing, written out as a picture.
	internal bool CaptureManaged( string path )
	{
		var bitmap = new Bitmap( Size.x.CeilToInt().Clamp( 1, 8192 ), Size.y.CeilToInt().Clamp( 1, 8192 ) );

		if ( !PaintInto( bitmap, new Rect( 0, Size ) ) ) return false;

		System.IO.File.WriteAllBytes( path, bitmap.ToPng() );

		return true;
	}

	//The topmost item under a point of the scene.
	internal GraphicsItem ItemAtManaged( Vector2 scenePosition )
	{
		foreach ( var item in items.OrderByDescending( x => x.ZIndex ).ToArray() )
		{
			var hit = item.DeepestAtManaged( scenePosition );

			if ( hit is not null ) return hit;
		}

		return null;
	}

	internal void SelectManaged( GraphicsItem item, bool alone )
	{
		if ( alone )
		{
			foreach ( var other in selected.ToArray() )
			{
				if ( other == item ) continue;

				other.SetSelectedManaged( false );
			}

			selected.Clear();
		}

		if ( item is null )
		{
			OnSelectionChanged?.Invoke();
			return;
		}

		if ( !selected.Contains( item ) ) selected.Add( item );

		item.SetSelectedManaged( true );

		OnSelectionChanged?.Invoke();
	}

	internal void DeselectManaged( GraphicsItem item )
	{
		if ( item is null || !selected.Remove( item ) ) return;

		item.SetSelectedManaged( false );

		OnSelectionChanged?.Invoke();
	}

	//Drawing: the whole scene, moved and scaled to where the view is looking.

	protected override void OnPaint()
	{
		if ( !OnPanels ) { base.OnPaint(); return; }

		Paint.Antialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( LocalRect );

		if ( BackgroundManaged is not null )
			Paint.DrawTiled( LocalRect, BackgroundManaged, FromSceneManaged( 0 ), _scale );

		var showing = SceneRectManaged;

		foreach ( var item in items.OrderBy( x => x.ZIndex ) )
		{
			if ( !item.IsValid ) continue;

			var where = item.SceneBoundsManaged;

			//Nothing outside the window is worth the paint call.
			if ( where.Right < showing.Left || where.Left > showing.Right ) continue;
			if ( where.Bottom < showing.Top || where.Top > showing.Bottom ) continue;

			Paint.Push();

			var at = FromSceneManaged( item.Position );

			Paint.Translate( at );
			Paint.Scale( _scale.x * item.Scale, _scale.y * item.Scale );

			if ( MathF.Abs( item.Rotation ) > 0.001f ) Paint.Rotate( item.Rotation );

			item.PaintManaged();

			Paint.Pop();
		}
	}

	//The mouse: items first, and the view itself where nothing was hit.

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMousePress( e ); return; }

		var scene = ToSceneManaged( e.LocalPosition );

		pressedAt = e.LocalPosition;

		//The middle button moves the view and does nothing else, over empty space or over anything else. It used to be offered to whatever was under it first,
		//and an item that only asks "was a button pressed on me" took it for a left click - so wheel-dragging from a wire grabbed the wire instead of the view.
		if ( e.MiddleMouseButton )
		{
			pressed = null;
			dragging = null;
			dragStarts.Clear();
			panning = true;
			return;
		}

		pressed = ItemAtManaged( scene );

		if ( pressed is null )
		{
			//Nothing under the pointer, and the left button clears the selection.
			panning = false;

			if ( e.LeftMouseButton ) SelectManaged( null, alone: true );
			return;
		}

		pressed.MousePressManaged( scene, in e );

		//Clicking a child - the plug on a node - selects the node it belongs to, unless the child itself asked to be
		//one or the other. A child that is not movable (a plug starting a wire) is not a reason to drag the node.
		var chosen = pressed;

		while ( chosen is not null && !chosen.Selectable && !chosen.Movable ) chosen = chosen.Parent;

		if ( chosen is not null && chosen.Selectable ) SelectManaged( chosen, alone: !e.HasCtrl && !chosen.Selected );

		dragging = pressed.Movable ? pressed : null;

		BeginDrag( e.LocalPosition );
	}

	//An item is being dragged about. For a view that wants to say what letting go here would do.
	protected virtual void OnItemDragged( GraphicsItem item ) { }

	//And it has been let go.
	protected virtual void OnItemDropped( GraphicsItem item ) { }

	//Remembers where everything about to be dragged is standing.
	private void BeginDrag( Vector2 at )
	{
		dragStarts.Clear();
		dragFrom = at;

		if ( dragging is null ) return;

		var moving = selected.Count > 0 && selected.Contains( dragging )
			? selected.ToList()
			: new List<GraphicsItem> { dragging };

		foreach ( var item in moving )
		{
			if ( item.Movable ) dragStarts.Add( (item, item.Position) );
		}
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseMove( e ); return; }

		if ( panning )
		{
			centre -= (e.LocalPosition - pressedAt) / _scale;
			pressedAt = e.LocalPosition;

			Update();
			return;
		}

		var scene = ToSceneManaged( e.LocalPosition );

		if ( pressed is not null )
		{
			if ( dragging is not null && dragging.Movable )
			{
				//From where the press was, not from the last event: an item that rounds where it
				//is put must not be told a rounded position and then asked to move on from there.
				var moved = (e.LocalPosition - dragFrom) / _scale;

				foreach ( var (item, start) in dragStarts )
				{
					if ( item.IsValid() && item.Movable ) item.Position = start + moved;
				}

				OnItemDragged( dragging );

				Update();
			}

			pressed.MouseMoveManaged( scene, in e );
			return;
		}

		var under = ItemAtManaged( scene );

		if ( under == hovered ) return;

		hovered?.HoverManaged( false );
		hovered = under;
		hovered?.HoverManaged( true );

		Update();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseReleased( e ); return; }

		panning = false;

		pressed?.MouseReleaseManaged( ToSceneManaged( e.LocalPosition ), in e );

		if ( dragging is not null ) OnItemDropped( dragging );

		pressed = null;
		dragging = null;
		dragStarts.Clear();
	}

	protected override void OnMouseLeave()
	{
		if ( !OnPanels ) { base.OnMouseLeave(); return; }

		hovered?.HoverManaged( false );
		hovered = null;

		base.OnMouseLeave();
	}

	protected override void OnMouseWheel( WheelEvent e )
	{
		if ( !OnPanels ) { base.OnMouseWheel( e ); return; }

		//Zoom about the pointer, so what is under it stays under it.
		var before = ToSceneManaged( e.Position );

		var by = e.Delta > 0 ? 0.9f : 1.1f;

		ScaleManaged = _scale * by;

		var after = ToSceneManaged( e.Position );

		centre += before - after;

		e.Accept();
		Update();
	}
}
