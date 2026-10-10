using Rinten.UI;

namespace Editor.NodeEditor;

public interface IGridSizeView
{
	float GridSize { get; }
}

/// <summary>Graph view: a pan/zoom canvas holding a world of flexbox nodes, with wires on a layer below</summary>
[StyleSheet( "GraphView.cs.scss" )]
public partial class GraphView : Panel, IGridSizeView, IEditorDropTarget
{
	/// <summary>Where the view is looking, and how close.</summary>
	public GraphViewport Viewport { get; } = new();

	/// <summary>Everything placed in the graph: nodes, comments, grips, the rubber band.</summary>
	public IEnumerable<GraphItem> Items => _items;

	/// <summary>Every wire.</summary>
	public IReadOnlyList<Connection> Connections => _connections;

	/// <summary>What is selected, wires included.</summary>
	public IEnumerable<IGraphSelectable> SelectedItems => _selection;

	/// <summary>Raised whenever the selection changes.</summary>
	public event Action OnSelectionChanged;

	public float GridSize { get; set; } = 12.0f;

	public virtual ConnectionStyle ConnectionStyle => ConnectionStyle.Default;

	//Where this view's place is remembered between sessions.
	protected virtual string ViewCookie => null;

	private readonly List<GraphItem> _items = new();
	private readonly List<Connection> _connections = new();
	private readonly List<IGraphSelectable> _selection = new();

	//The part that moves: one transform for everything in the graph.
	private readonly Panel _world;

	private readonly GraphBackground _background;
	private readonly ConnectionLayer _connectionLayer;

	private ConnectionStyle _lastConnectionStyle;

	public GraphView()
	{
		AddClass( "graph-view" );

		AcceptsFocus = true;

		_background = AddChild( new GraphBackground( this ) );

		_world = AddChild<Panel>( "graph-world" );

		_connectionLayer = _world.AddChild( new ConnectionLayer( this ) );

		Viewport.Changed += OnViewportChanged;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		Viewport.Changed -= OnViewportChanged;
	}

	//Where the view is looking

	public Vector2 Center
	{
		get => Viewport.Centre;
		set => Viewport.Centre = value;
	}

	public float Scale
	{
		get => Viewport.Zoom;
		set => Viewport.Zoom = value;
	}

	public Vector2 ToScene( Vector2 view ) => Viewport.ToScene( view );
	public Vector2 FromScene( Vector2 scene ) => Viewport.FromScene( scene );

	public void CenterOn( Vector2 scene ) => Viewport.Centre = scene;

	public void FitInView( Rect scene, float margin = 32.0f ) => Viewport.FitInView( scene, margin );

	/// <summary>Jump the view to a place and a scale, cancelling any easing.</summary>
	public void SetView( Vector2 center, float scale )
	{
		_zoomTarget = 0.0f;

		Viewport.Zoom = scale;
		Viewport.Centre = center;
	}

	public void CenterOnSelection()
	{
		var bounds = new Rect();
		var any = false;

		foreach ( var item in _selection )
		{
			if ( !item.IsValid() ) continue;

			var rect = item.SelectionRect;

			bounds = any ? bounds.AddPoint( rect.TopLeft ).AddPoint( rect.BottomRight ) : rect;
			any = true;
		}

		if ( !any ) return;

		CenterOn( bounds.Center );
	}

	public void RestoreViewFromCookie()
	{
		if ( ViewCookie is not { } cookie ) return;

		Viewport.Zoom = EditorCookie.Get( $"{cookie}.view.scale", Viewport.Zoom );
		Viewport.Centre = EditorCookie.Get( $"{cookie}.view.center", Viewport.Centre );
	}

	private void SaveViewToCookie()
	{
		if ( ViewCookie is not { } cookie ) return;

		EditorCookie.Set( $"{cookie}.view.center", Viewport.Centre );
		EditorCookie.Set( $"{cookie}.view.scale", Viewport.Zoom );
	}

	//Writes where the view is looking onto the world's transform.
	private void OnViewportChanged()
	{
		var transform = new PanelTransform();

		//Added last is applied first, as in CSS - so this is a scale about the canvas corner and then a slide, which is what the viewport describes.
		transform.AddTranslate( Length.Pixels( Viewport.Offset.x ), Length.Pixels( Viewport.Offset.y ) );
		transform.AddScale( Viewport.Zoom );

		_world.Style.Transform = transform;

		_background.MarkRenderDirty();
		_connectionLayer.MarkRenderDirty();
	}

	//What is in the graph

	/// <summary>Puts an item in the graph.</summary>
	public T AddItem<T>( T item ) where T : GraphItem
	{
		if ( item is null || _items.Contains( item ) ) return item;

		_items.Add( item );
		_world.AddChild( item );

		return item;
	}

	//Takes an item out of the graph, without destroying it.
	internal void ForgetItem( GraphItem item )
	{
		_items.Remove( item );

		if ( item is IGraphSelectable selectable ) _selection.Remove( selectable );
	}

	internal void ForgetConnection( Connection connection )
	{
		_connections.Remove( connection );
		_selection.Remove( connection );
	}

	//A grip for one of a wire's bends.
	internal ConnectionHandle AddHandle( Connection connection ) => AddItem( new ConnectionHandle( this, connection ) );

	//The wires, hovered and selected ones last so they lie over the rest.
	internal IEnumerable<Connection> DrawOrderedConnections
	{
		get
		{
			foreach ( var connection in _connections )
			{
				if ( connection.Hovered || connection.Selected ) continue;

				yield return connection;
			}

			foreach ( var connection in _connections )
			{
				if ( !connection.Selected || connection.Hovered ) continue;

				yield return connection;
			}

			foreach ( var connection in _connections )
			{
				if ( !connection.Hovered ) continue;

				yield return connection;
			}

			if ( Preview.IsValid() ) yield return Preview;
		}
	}

	/// <summary>The wires need drawing again.</summary>
	public void RedrawConnections() => _connectionLayer?.MarkRenderDirty();

	/// <summary>The widget this view is shown in, where it has been put in one.</summary>
	public PanelDockHost Host { get; internal set; }

	/// <summary>Whether the view is one somebody can see: its widget is up, and nothing has hidden the panel itself.</summary>
	public bool IsShown => Host is not null ? Host.Visible : IsVisible;

	/// <summary>Says everything in the graph needs re-reading.</summary>
	public void Update()
	{
		foreach ( var node in Items.OfType<NodeUI>() )
		{
			node.Update();
		}

		RedrawConnections();
	}

	public void DeleteAllItems()
	{
		foreach ( var item in _items.ToArray() )
		{
			item.Destroy();
		}

		_items.Clear();
		_selection.Clear();
	}

	//Selection

	public void Select( IGraphSelectable item, bool alone )
	{
		if ( alone )
		{
			foreach ( var other in _selection.ToArray() )
			{
				if ( other == item ) continue;

				other.Selected = false;
			}

			_selection.Clear();
		}

		if ( item is not null && item.Selectable && !_selection.Contains( item ) )
		{
			_selection.Add( item );
			item.Selected = true;
		}

		OnSelectionChanged?.Invoke();
	}

	public void Deselect( IGraphSelectable item )
	{
		if ( item is null || !_selection.Remove( item ) ) return;

		item.Selected = false;

		OnSelectionChanged?.Invoke();
	}

	public void SelectAll()
	{
		foreach ( var item in _items )
		{
			if ( !item.Selectable || _selection.Contains( item ) ) continue;

			item.Selected = true;
			_selection.Add( item );
		}

		OnSelectionChanged?.Invoke();
	}

	public void ClearSelection()
	{
		foreach ( var item in _selection.ToArray() )
		{
			item.Selected = false;
		}

		_selection.Clear();

		OnSelectionChanged?.Invoke();
	}

	//Everything a rubber band covers, selected.
	internal void SelectInRect( Rect rect, bool add )
	{
		var changed = false;

		foreach ( var item in _items )
		{
			if ( !item.Selectable ) continue;

			var hit = item.SelectionRect.IsInside( rect, false );

			if ( hit && !_selection.Contains( item ) )
			{
				item.Selected = true;
				_selection.Add( item );
				changed = true;
			}
			else if ( !hit && !add && _selection.Contains( item ) )
			{
				item.Selected = false;
				_selection.Remove( item );
				changed = true;
			}
		}

		if ( changed ) OnSelectionChanged?.Invoke();
	}

	//Handle colours

	public HandleConfig DefaultHandleConfig { get; } = new( null, Color.Parse( "#999" )!.Value );

	private Dictionary<Type, HandleConfig> HandleConfigCache { get; } = new();

	protected virtual HandleConfig OnGetHandleConfig( Type type ) => DefaultHandleConfig;

	public HandleConfig GetHandleConfig( Type t )
	{
		if ( HandleConfigCache.TryGetValue( t, out var config ) ) return config;

		config = OnGetHandleConfig( t );

		return HandleConfigCache[t] = config with { Name = config.Name ?? t.ToSimpleString( false ).HtmlEncode() };
	}

	public override void Tick()
	{
		base.Tick();

		Viewport.Size = Box.Rect.Size;

		SmoothZoom();
		DriveInteraction();

		if ( _lastConnectionStyle != ConnectionStyle )
		{
			_lastConnectionStyle = ConnectionStyle;

			foreach ( var connection in _connections )
			{
				connection.Layout();
			}
		}
	}
}
