using Rinten;
using System;
using System.Linq;

namespace Editor;

//Where it is, how big, which way round, whether it is selected - and its own paint, which is the item's business and is not touched here. The view above draws these in order and hands them the mouse.
/// <summary>One thing in a graphics scene.</summary>
public abstract partial class GraphicsItem
{
	private GraphicsView view;

	private Vector2 position;
	private float rotation;
	private float itemScale = 1.0f;
	private float zIndex;

	private bool movable;
	private bool selectable;
	private bool focusable;
	private bool hoverEvents;
	private bool clip;
	private bool clipChildren;

	private bool selected;
	private bool hovered;
	private bool destroyed;

	internal bool OnPanels => EditorBackend.Panels;

	//One of ours, with nothing native behind it.
	internal void MakeManaged( GraphicsItem parent )
	{
		if ( parent is null ) return;

		//The parent property does the bookkeeping either way round, so the children list is the same one the rest of the class already uses.
		Parent = parent;
	}

	internal GraphicsView ViewManaged => view;

	internal void SetViewManaged( GraphicsView wanted )
	{
		if ( view == wanted ) return;

		view = wanted;

		foreach ( var child in Children ) child.SetViewManaged( wanted );
	}

	internal GraphicsItem ParentManaged => Parent;


	internal Vector2 PositionManaged
	{
		get => position;
		set
		{
			if ( position == value ) return;

			position = value;

			//After a move: snap, write position back, re-lay wires; re-entry from snapping is stopped by the guard
			OnPositionChanged();

			view?.Update();
		}
	}

	internal float RotationManaged
	{
		get => rotation;
		set { rotation = value; view?.Update(); }
	}

	internal float ScaleManaged
	{
		get => itemScale;
		set { itemScale = value; view?.Update(); }
	}

	internal float ZIndexManaged
	{
		get => zIndex;
		set { zIndex = value; view?.Update(); }
	}

	internal bool MovableManaged { get => movable; set => movable = value; }
	internal bool SelectableManaged { get => selectable; set => selectable = value; }
	internal bool FocusableManaged { get => focusable; set => focusable = value; }
	internal bool HoverEventsManaged { get => hoverEvents; set => hoverEvents = value; }
	internal bool ClipManaged { get => clip; set => clip = value; }
	internal bool ClipChildrenManaged { get => clipChildren; set => clipChildren = value; }

	internal bool SelectedManaged => selected;
	internal bool HoveredManaged => hovered;
	internal bool IsValidManaged => !destroyed;

	internal void SetSelectedManaged( bool value )
	{
		if ( selected == value ) return;

		selected = value;

		OnSelectionChanged();

		view?.Update();
	}

	internal void DestroyManaged()
	{
		if ( destroyed ) return;

		destroyed = true;

		foreach ( var child in Children.ToArray() ) child.Destroy();

		Parent = null;

		view?.RemoveManaged( this );

		OnDestroy();
	}

	//Hit test: cheap bounding rect first, then the item's own Contains for its real shape (e.g. wires)
	internal bool HitTestManaged( Vector2 scene )
	{
		var local = ToItemManaged( scene );

		return BoundingRect.IsInside( local ) && Contains( local );
	}

	//The deepest thing under a point of the scene: this item, or the child of it that is over the rest. Children are asked first and in reverse, so the
	//one drawn last is the one that answers - a node's plugs are children of the node, and clicking one has to give you the plug and not the box behind it.
	internal GraphicsItem DeepestAtManaged( Vector2 scene )
	{
		if ( !IsValid ) return null;
		if ( ClipChildren && !HitTestManaged( scene ) ) return null;

		foreach ( var child in Children.Reverse() )
		{
			var hit = child.DeepestAtManaged( scene );

			if ( hit is not null ) return hit;
		}

		return HitTestManaged( scene ) ? this : null;
	}

	//Between the item's own coordinates and everyone else's. An item sits at a position inside its
	//parent, at a scale of its own; the scene is what you get after walking that all the way up.

	private float SafeScale => itemScale <= 0 ? 1.0f : itemScale;

	//A point of this item, in its parent's coordinates.
	internal Vector2 ToParentManaged( Vector2 local ) => (local * SafeScale) + position;

	//And a point of the parent, in this item's.
	internal Vector2 FromParentManaged( Vector2 parent ) => (parent - position) / SafeScale;

	//A point of this item, in the scene.
	internal Vector2 ToSceneManaged( Vector2 local )
	{
		var at = ToParentManaged( local );

		return ParentManaged is null ? at : ParentManaged.ToSceneManaged( at );
	}

	//And a point of the scene, in this item's.
	internal Vector2 FromSceneManaged( Vector2 scene ) => ToItemManaged( scene );

	private Vector2 ToItemManaged( Vector2 scene )
	{
		var at = ParentManaged is null ? scene : ParentManaged.ToItemManaged( scene );

		return FromParentManaged( at );
	}

	//The item's own paint, with the state it is in - which is what decides how most of them draw themselves.
	internal void PaintManaged()
	{
		Paint.SetFlags( selected, hovered, false, focusable && selected, true );
		Paint.LocalRect = BoundingRect;

		OnPaint();

		foreach ( var child in Children )
		{
			if ( !child.IsValid ) continue;

			Paint.Push();
			Paint.Translate( child.Position );

			if ( child.Scale != 1.0f ) Paint.Scale( child.Scale, child.Scale );
			if ( MathF.Abs( child.Rotation ) > 0.001f ) Paint.Rotate( child.Rotation );

			child.PaintManaged();

			Paint.Pop();
		}
	}

	//This item's box in the scene, including how it is anchored.
	internal Rect SceneBoundsManaged
	{
		get
		{
			var local = BoundingRect;
			var a = ToSceneManaged( local.Position );
			var b = ToSceneManaged( local.Position + local.Size );
			var min = Vector2.Min( a, b );
			var max = Vector2.Max( a, b );

			return new Rect( min, max - min );
		}
	}

	internal void HoverManaged( bool over )
	{
		hovered = over;
		Hovered = over;

		if ( !hoverEvents ) return;

		if ( over ) OnHoverEnter( new GraphicsHoverEvent() );
		else OnHoverLeave( new GraphicsHoverEvent() );
	}

	internal void MousePressManaged( Vector2 scene, in MouseEvent source )
	{
		var e = EventFrom( scene, in source );

		OnMousePressed( e );
	}

	internal void MouseMoveManaged( Vector2 scene, in MouseEvent source )
	{
		var e = EventFrom( scene, in source );

		OnMouseMove( e );
	}

	internal void MouseReleaseManaged( Vector2 scene, in MouseEvent source )
	{
		var e = EventFrom( scene, in source );

		OnMouseReleased( e );
	}

	private GraphicsMouseEvent EventFrom( Vector2 scene, in MouseEvent source )
	{
		return new GraphicsMouseEvent
		{
			ScenePosition = scene,
			LocalPosition = FromSceneManaged( scene ),
			ScreenPosition = source.ScreenPosition,
			Button = source.Button,
			Buttons = source.ButtonState,
			KeyboardModifiers = source.KeyboardModifiers
		};
	}
}
