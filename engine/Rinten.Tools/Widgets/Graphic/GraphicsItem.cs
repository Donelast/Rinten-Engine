using Native;
using System;

namespace Editor
{
	public abstract partial class GraphicsItem : IValid
	{
		private readonly HashSet<GraphicsItem> _children = new();

		/// <summary>What is inside this item.</summary>
		public IEnumerable<GraphicsItem> Children => _children;


		public GraphicsItem( GraphicsItem parent = null )
		{
			MakeManaged( parent );
		}

		public GraphicsView GraphicsView => ViewManaged;

		GraphicsScene _scene;

		internal GraphicsScene Scene
		{
			get
			{
				if ( _scene is not null ) return _scene;
				if ( _parent is not null ) return _parent.Scene;

				return default;
			}

			set
			{
				_scene = value;
			}
		}

		GraphicsItem _parent;
		public GraphicsItem Parent
		{
			set
			{
				if ( _parent == value ) return;

				//old parent cleanup
				_parent?.RemoveChild( this );

				//change
				_parent = value;

				//new parent
				_parent?.AddChild( this );

				SetViewManaged( value?.ViewManaged );
			}
			get { return _parent; }
		}

		public void Destroy()
		{
			DestroyManaged();
		}

		protected virtual void OnDestroy()
		{

		}


		private void AddChild( GraphicsItem i ) => _children.Add( i );

		private void RemoveChild( GraphicsItem i ) => _children.Remove( i );

		bool IValid.IsValid => IsValid;
		public bool IsValid => IsValidManaged;



		public Vector2 Position
		{
			get => PositionManaged;
			set
			{
				PositionManaged = value;
			}
		}

		public Vector2 ViewPosition
		{
			get => GraphicsView.FromScene( ToScene( 0 ) );
		}

		public Vector2 Center
		{
			get => Position + Size * 0.5f;
		}

		public Rect LocalRect
		{
			get => new Rect( 0, Size );
		}

		public Rect SceneRect
		{
			get => new Rect( Position, Size );
			set
			{
				Position = value.Position;
				Size = value.Size;
			}
		}

		public float Rotation
		{
			get => RotationManaged;
			set
			{
				RotationManaged = value;
			}
		}

		public float Scale
		{
			get => ScaleManaged;
			set
			{
				ScaleManaged = value;
			}
		}

		public bool Movable
		{
			get => MovableManaged;
			set
			{
				MovableManaged = value;
			}
		}

		public bool ClipChildren
		{
			get => ClipChildrenManaged;
			set
			{
				ClipChildrenManaged = value;
			}
		}

		public bool Clip
		{
			get => ClipManaged;
			set
			{
				ClipManaged = value;
			}
		}

		string _toolTip;

		[Obsolete( $"Use {nameof( ToolTip )}" )]
		public string Tooltip
		{
			get => ToolTip;
			set => ToolTip = value;
		}

		public string ToolTip
		{
			get => _toolTip;
			set
			{
				if ( _toolTip == value )
					return;

				_toolTip = value;

				return;
			}
		}

		bool _selected;
		public bool Selected
		{
			get => _selected;
			set
			{
				if ( _selected == value ) return;

				//The view keeps the list of what is selected, so it is told rather than the item just setting a flag on itself.
				if ( value ) ViewManaged?.SelectManaged( this, alone: false );
				else ViewManaged?.DeselectManaged( this );

				if ( ViewManaged is null ) SetSelectedManaged( value );
			}
		}

		public bool Selectable
		{
			get => SelectableManaged;
			set
			{
				SelectableManaged = value;
			}
		}

		/// <summary>Gets keyboard input</summary>
		public bool Focusable
		{
			get => FocusableManaged;
			set
			{
				FocusableManaged = value;
			}
		}

		public bool HoverEvents
		{
			get => HoverEventsManaged;
			set
			{
				HoverEventsManaged = value;
			}
		}

		public float ZIndex
		{
			get => ZIndexManaged;
			set
			{
				ZIndexManaged = value;
			}
		}

		protected virtual void OnPaint()
		{

		}

		internal Rect InternalGetBoundingRect()
		{
			return BoundingRect;
		}

		internal Rect InternalGetPaintRect()
		{
			return new( -HandlePosition * Size, Size );
		}

		/// <summary>The outer bounds of the item as a rectangle; all painting must be restricted to inside an item's bounding rect.</summary>
		public virtual Rect BoundingRect => new( -HandlePosition * Size, Size );

		public virtual Vector2 Size
		{
			get;
			set;
		}

		public float Width
		{
			get => Size.x;
			set => Size = Size.WithX( value );
		}

		public float Height
		{
			get => Size.y;
			set => Size = Size.WithY( value );
		}

		/// <summary>0,0 means top left, 1,1 means bottom right</summary>
		public virtual Vector2 HandlePosition
		{
			get;
			set;
		}

		protected virtual void OnMouseReleased( GraphicsMouseEvent e )
		{

		}

		protected virtual void OnMousePressed( GraphicsMouseEvent e )
		{

		}

		protected virtual void OnMouseMove( GraphicsMouseEvent e )
		{

		}

		public bool Hovered { get; internal set; }

		protected virtual void OnHoverEnter( GraphicsHoverEvent e )
		{

		}

		protected virtual void OnHoverMove( GraphicsHoverEvent e )
		{

		}

		protected virtual void OnHoverLeave( GraphicsHoverEvent e )
		{

		}

		public void Update()
		{
			ViewManaged?.Update();
		}

		/// <summary>Usually called before resizing items so they paint properly.</summary>
		public void PrepareGeometryChange()
		{
			return;
		}

		public Vector2 ToScene( Vector2 pos ) => ToSceneManaged( pos );
		public Vector2 FromScene( Vector2 pos ) => FromSceneManaged( pos );

		public Vector2 ToParent( Vector2 pos ) => ToParentManaged( pos );
		public Vector2 FromParent( Vector2 pos ) => FromParentManaged( pos );

		public Vector2 ToItem( GraphicsItem item, Vector2 pos ) => item.FromSceneManaged( ToSceneManaged( pos ) );
		public Vector2 FromItem( GraphicsItem item, Vector2 pos ) => FromSceneManaged( item.ToSceneManaged( pos ) );

		internal void InternalItemChange( int change )
		{
			switch ( (GraphicsItemChange)change )
			{
				case GraphicsItemChange.ItemPositionHasChanged:
				case GraphicsItemChange.ItemTransformHasChanged:
					{
						OnPositionChanged();
						return;
					}
				case GraphicsItemChange.ItemMoved:
					{
						OnMoved();
						return;
					}

				case GraphicsItemChange.ItemSelectedHasChanged:
					{
						OnSelectionChanged();
						return;
					}

			}
		}

		protected virtual void OnPositionChanged()
		{

		}

		//Item has been moved by the user dragging it
		protected virtual void OnMoved()
		{

		}

		protected virtual void OnSelectionChanged()
		{
			_selected = SelectedManaged;

			Scene?.OnItemSelectionChangedInternal( this, _selected );
		}

		CursorShape _cursor = CursorShape.None;
		public CursorShape Cursor
		{
			get => _cursor;
			set
			{
				_cursor = value;

				return;
			}
		}

		public Rinten.Bind.Builder Bind( string targetName )
		{
			var bb = new Rinten.Bind.Builder
			{
				system = Rinten.Internal.GlobalToolsNamespace.BindSystem
			};

			return bb.Set( this, targetName );
		}

		//A key has been pressed.
		protected virtual void OnKeyPress( KeyEvent e )
		{

		}

		//A key has been released.
		protected virtual void OnKeyRelease( KeyEvent e )
		{

		}

		/// <summary>Whether a point inside the bounding rect is on the item itself.</summary>
		public virtual bool Contains( Vector2 localPos )
		{
			return true;
		}

	}

}



namespace Native
{
	enum GraphicsItemFlag
	{
		ItemIsMovable = 0x1,
		ItemIsSelectable = 0x2,
		ItemIsFocusable = 0x4,
		ItemClipsToShape = 0x8,
		ItemClipsChildrenToShape = 0x10,
		ItemIgnoresTransformations = 0x20,
		ItemIgnoresParentOpacity = 0x40,
		ItemDoesntPropagateOpacityToChildren = 0x80,
		ItemStacksBehindParent = 0x100,
		ItemUsesExtendedStyleOption = 0x200,
		ItemHasNoContents = 0x400,
		ItemSendsGeometryChanges = 0x800,
		ItemAcceptsInputMethod = 0x1000,
		ItemNegativeZStacksBehindParent = 0x2000,
		ItemIsPanel = 0x4000,
		ItemIsFocusScope = 0x8000, //internal
		ItemSendsScenePositionChanges = 0x10000,
		ItemStopsClickFocusPropagation = 0x20000,
		ItemStopsFocusHandling = 0x40000,
		ItemContainsChildrenInShape = 0x80000
		//NB! Don't forget to increase the d_ptr->flags bit field by 1 when adding a new flag.
	};

	enum GraphicsItemChange
	{
		ItemPositionChange,

		ItemVisibleChange = 2,
		ItemEnabledChange,
		ItemSelectedChange,
		ItemParentChange,
		ItemChildAddedChange,
		ItemChildRemovedChange,
		ItemTransformChange,
		ItemPositionHasChanged,
		ItemTransformHasChanged,
		ItemSceneChange,
		ItemVisibleHasChanged,
		ItemEnabledHasChanged,
		ItemSelectedHasChanged,
		ItemParentHasChanged,
		ItemSceneHasChanged,
		ItemCursorChange,
		ItemCursorHasChanged,
		ItemToolTipChange,
		ItemToolTipHasChanged,
		ItemFlagsChange,
		ItemFlagsHaveChanged,
		ItemZValueChange,
		ItemZValueHasChanged,
		ItemOpacityChange,
		ItemOpacityHasChanged,
		ItemScenePositionHasChanged,
		ItemRotationChange,
		ItemRotationHasChanged,
		ItemScaleChange,
		ItemScaleHasChanged,
		ItemTransformOriginPointChange,
		ItemTransformOriginPointHasChanged,
		ItemMoved
	}
}
