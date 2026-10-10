using System;

namespace Editor
{
	public enum ScrollbarMode
	{
		/// <summary>Only show the scrollbar when necessary.</summary>
		Auto,

		/// <summary>Never show the scrollbar.</summary>
		Off,

		/// <summary>Always show the scrollbar.</summary>
		On
	}

	public partial class GraphicsView : Widget
	{
		/// <summary>The part of the scene the view is showing, in scene coordinates.</summary>
		public Rect SceneRect
		{
			get => SceneRectManaged;
			set => FitInViewManaged( value );
		}


		public bool Capture( string path )
		{
			return CaptureManaged( path );
		}

		/// <summary>All items inside this rect will be selected</summary>
		public Rect SelectionRect
		{
			set
			{
				SelectInRectManaged( value );
			}
		}

		/// <summary>Where in the scene is the view currently centered.</summary>
		public Vector2 Center
		{
			get => CentreManaged;
			set => CenterOn( value );
		}

		Vector2 _scale = 1;
		public Vector2 Scale
		{
			get => _scale;
			set
			{
				ScaleManaged = value;
			}
		}

		float _rotate;
		public float Rotation
		{
			get => _rotate;
			set
			{
				_rotate = value;

				Update();
			}
		}

		public IEnumerable<GraphicsItem> SelectedItems => SelectedManaged;

		public float MinZoom { get; set; } = 0.1f;
		public float MaxZoom { get; set; } = 5.0f;

		public void Zoom( float adjust, Vector2 viewpos )
		{
			ZoomManaged( adjust, viewpos );
		}

		public void Translate( Vector2 delta )
		{
			if ( delta.Length <= 0.001f )
				return;

			CentreManaged -= delta;
		}

		void BuildTransform()
		{
		}

		public GraphicsView( Widget parent = null ) : base()
		{
			MakeSurface( parent );

			SetSizeMode( SizeMode.Flexible, SizeMode.Flexible );

			MouseTracking = true;
			FocusMode = FocusMode.Click;

			Scene = new GraphicsScene( this );
		}

		public Vector2 ToScene( Vector2 pos ) => ToSceneManaged( pos );
		public Rect ToScene( Rect pos ) { pos.Position = ToScene( pos.Position ); return pos; }

		public Vector2 FromScene( Vector2 pos ) => FromSceneManaged( pos );
		public Rect FromScene( Rect pos ) { pos.Position = FromScene( pos.Position ); return pos; }

		internal GraphicsScene Scene
		{
			get;
			init;
		}

		public void DeleteAllItems()
		{
			ClearManaged();
		}

		public IEnumerable<GraphicsItem> Items => ItemsManaged;

		public void CenterOn( Vector2 center )
		{
			CentreManaged = center;
		}

		public void FitInView( Rect rect )
		{
			FitInViewManaged( rect );
		}

		private ScrollbarMode horizontalScrollbar = ScrollbarMode.Off;
		private ScrollbarMode verticalScrollbar = ScrollbarMode.Off;

		public ScrollbarMode HorizontalScrollbar
		{
			//A graphics view of ours has no bars of its own - it is panned and zoomed with the mouse - so the setting is kept and nothing else.
			get => horizontalScrollbar;
			set
			{
				horizontalScrollbar = value;
			}
		}

		public ScrollbarMode VerticalScrollbar
		{
			get => verticalScrollbar;
			set
			{
				verticalScrollbar = value;
			}
		}

		public void SetBackgroundImage( string image )
		{
			SetBackgroundImage( Pixmap.FromFile( image ) );
		}

		public void SetBackgroundImage( Pixmap image )
		{
			BackgroundManaged = image;
		}


		public enum ViewportAnchorType
		{
			NoAnchor,
			AnchorViewCenter,
			AnchorUnderMouse
		}

		private ViewportAnchorType transformAnchor = ViewportAnchorType.NoAnchor;

		public ViewportAnchorType TransformAnchor
		{
			get => transformAnchor;
			set
			{
				transformAnchor = value;
			}
		}

		public GraphicsItem GetItemAt( Vector2 scenePosition )
		{
			return ItemAtManaged( scenePosition );
		}

		public void Add( GraphicsItem t )
		{
			AddManaged( t );
		}

		//Everything the view is holding, gone.

		public Action OnSelectionChanged { get; set; }


		public enum DragTypes
		{
			None,
			Scroll,
			SelectionRect
		}

		DragTypes _dragType = DragTypes.None;

		/// <summary>What happens when the user drags the mouse. You generally want to toggle this in OnMouseDown to switch what happens with different mouse buttons.</summary>
		public DragTypes DragType
		{
			get => _dragType;
			set
			{
				_dragType = value;

				return;
			}
		}
	}
}
