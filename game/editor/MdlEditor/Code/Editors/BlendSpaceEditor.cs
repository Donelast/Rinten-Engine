using Editor.MdlEditor.Nodes;
using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Full-page 2D blend space editor: the canvas beside its own preview, split by a SplitterPanel
[NodePage( typeof( Blend2DNode ) )]
[StyleSheet( "BlendSpaceEditor.cs.scss" )]
internal sealed class BlendSpacePage : Panel, INodePage
{
	//Where the divider was left. The shares are the whole of the state now, so this reads "0.6 0.4" rather than
	//a splitter widget's own opaque blob - an older value has a different number of cells in it and is ignored.
	private const string SplitterCookie = "AnimGraph.BlendSpaceSplitter";

	private readonly BlendSpaceCanvas _canvas;
	private readonly BlendSpacePreview _preview;
	private readonly SplitterPanel _splitter;

	public CanvasPanel ViewCanvas => _canvas;
	public bool SupportsSecondaryView => true;

	public BlendSpacePage( Blend2DNode node, Window window )
	{
		AddClass( "blend-space-page" );

		_splitter = AddChild( new SplitterPanel( false ) );

		_canvas = new BlendSpaceCanvas( node, window );
		_preview = new BlendSpacePreview( node, window );

		_splitter.AddCell( _canvas );
		_splitter.AddCell( _preview );

		_splitter.RestoreState( ProjectCookie.Get<string>( SplitterCookie, null ) );

		_canvas.BlendValueChanged += _preview.SetBlendValue;
		_canvas.PointsChanged += _preview.MarkDirty;
	}

	public override void OnDeleted()
	{
		ProjectCookie.Set( SplitterCookie, _splitter.SaveState() );

		base.OnDeleted();
	}

	public bool Rebind( Node node )
	{
		if ( !_canvas.Rebind( node ) )
			return false;

		_preview.Rebind( (Blend2DNode)node );
		return true;
	}

	public void OnGraphEdited()
	{
		_canvas.Repaint();
		_preview.MarkDirty();
	}
}

//Blend space canvas with draggable points and a blend handle that scrubs the page preview
internal sealed class BlendSpaceCanvas : CanvasPanel
{
	private Blend2DNode _node;

	internal static BlendSpaceCanvas Current { get; private set; }
	internal Blend2DNode Node => _node;

	public int Selected { get; private set; } = -1;
	public Blend2DNode.BlendPoint SelectedPoint => Selected >= 0 && Selected < _node.Points.Count ? _node.Points[Selected] : null;

	private float _gridStep = 1;

	private int _dragIndex = -1;
	private int _hoverIndex = -1;

	private Vector2 _blendValue;
	private bool _draggingHandle;
	private bool _hoverHandle;

	internal event Action<Vector2> BlendValueChanged;
	internal event Action PointsChanged;

	protected override bool FlipY => true;
	protected override float MaxZoom => 5000;

	//Zoom floor adapts to the points — any value range can always be framed with room to spare, while unit-scale spaces keep a sane limit.
	protected override float MinZoom
	{
		get
		{
			var min = new Vector2( float.MaxValue );
			var max = new Vector2( float.MinValue );

			foreach ( var point in _node.Points )
			{
				min = Vector2.Min( min, point.Position );
				max = Vector2.Max( max, point.Position );
			}

			var span = _node.Points.Count > 0 ? MathF.Max( MathF.Max( max.x - min.x, max.y - min.y ), 0.5f ) : 0.5f;

			return MathF.Min( 10, MathF.Min( Width, Height ) * 0.25f / span );
		}
	}

	public BlendSpaceCanvas( Blend2DNode node, Window window ) : base( window )
	{
		_node = node;

		AddClass( "blend-space-canvas" );

		Current = this;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		if ( Current == this )
			Current = null;
	}

	protected override void Fit()
	{
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );

		foreach ( var point in _node.Points )
		{
			min = Vector2.Min( min, point.Position );
			max = Vector2.Max( max, point.Position );
		}

		if ( _node.Points.Count == 0 )
		{
			min = new Vector2( -1 );
			max = new Vector2( 1 );
		}

		var span = Vector2.Max( max - min, new Vector2( 0.5f ) ) * 1.4f;
		FrameView( (min + max) * 0.5f, MathF.Min( Width / span.x, Height / span.y ) );
	}

	private int HitPoint( Vector2 local )
	{
		for ( var i = _node.Points.Count - 1; i >= 0; i-- )
		{
			if ( ToCanvas( _node.Points[i].Position ).Distance( local ) <= 14 )
				return i;
		}

		return -1;
	}

	private bool HitHandle( Vector2 local ) => ToCanvas( _blendValue ).Distance( local ) <= 10;

	private void SetBlendValue( Vector2 value )
	{
		if ( _blendValue == value )
			return;

		_blendValue = value;
		BlendValueChanged?.Invoke( value );
		Repaint();
	}

	private void Select( int index )
	{
		if ( Selected == index )
			return;

		Selected = index;
		Editor.OnCanvasSelection( _node, SelectedPoint );
		Repaint();
	}

	internal void SelectForTest( int index ) => Select( index );

	/// <summary>Points this canvas at the same node in a rebuilt graph, keeping the view and selection - no panel churn, no flicker.</summary>
	public bool Rebind( Node node )
	{
		if ( node is not Blend2DNode blend || blend.Identifier != _node.Identifier )
			return false;

		_node = blend;

		if ( Selected >= blend.Points.Count )
			Selected = -1;

		Editor.OnCanvasSelection( _node, SelectedPoint );
		Repaint();
		return true;
	}

	private void Changed()
	{
		_node.Update();
		Editor.MarkDirty();
		PointsChanged?.Invoke();
		Repaint();
	}

	private void AddPoint( Vector2 position )
	{
		Editor.PushGraphUndo( "Add Blend Point" );
		_node.AddPoint( position );
		Changed();
		Editor.PushGraphRedo();
		Select( _node.Points.Count - 1 );
	}

	private void RemovePoint( int index )
	{
		if ( _node.Points.Count <= 3 )
			return;

		Editor.PushGraphUndo( "Remove Blend Point" );
		_node.RemovePoint( _node.Points[index] );
		Changed();
		Editor.PushGraphRedo();
		Select( -1 );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		//Takes the keyboard and starts a middle-button pan.
		base.OnMouseDown( e );

		var local = LocalMouse;

		if ( e.MouseButton == MouseButtons.Left )
		{
			if ( HitHandle( local ) )
			{
				_draggingHandle = true;
				e.StopPropagation();
				return;
			}

			var hit = HitPoint( local );
			var canDrag = hit >= 0 && hit == Selected;
			Select( hit );

			if ( canDrag )
			{
				_dragIndex = hit;
				Editor.PushGraphUndo( "Move Blend Point" );
			}
			else if ( hit < 0 )
			{
				StartPan( local, MouseButtons.Left );
			}

			e.StopPropagation();
			return;
		}

		if ( e.MouseButton == MouseButtons.Right )
		{
			OpenContextMenu( local );
			e.StopPropagation();
		}
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left )
			return;

		var local = LocalMouse;
		var hit = HitPoint( local );

		if ( hit >= 0 )
		{
			EndPointDrag();
			Select( hit );
			Editor.NavigateInto( SelectedPoint.Graph );
			e.StopPropagation();
			return;
		}

		if ( HitHandle( local ) )
			return;

		AddPoint( Snap( FromCanvas( local ), true ) );
		e.StopPropagation();
	}

	/// <summary>Both drags are read here rather than from a move event: the pointer wanders off the canvas while something is being dragged, and a panel is told about neither a move nor a button coming up somewhere else.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( !_draggingHandle && _dragIndex < 0 )
			return;

		var local = LocalMouse;
		var snap = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl );

		if ( _draggingHandle )
			SetBlendValue( Snap( FromCanvas( local ), snap ) );
		else
			DragPointTo( Snap( FromCanvas( local ), snap ) );

		if ( Application.MouseButtons.HasFlag( MouseButtons.Left ) )
			return;

		_draggingHandle = false;
		EndPointDrag();
	}

	private void DragPointTo( Vector2 position )
	{
		if ( _dragIndex >= _node.Points.Count )
			return;

		//Once a frame rather than once a move, so the graph is only told about it when the point has actually gone somewhere.
		if ( _node.Points[_dragIndex].Position == position )
			return;

		_node.Points[_dragIndex].Position = position;
		Changed();
	}

	private void EndPointDrag()
	{
		if ( _dragIndex < 0 )
			return;

		_dragIndex = -1;
		Editor.PushGraphRedo();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		if ( _draggingHandle || _dragIndex >= 0 || IsPanning )
			return;

		var local = LocalMouse;
		var hover = HitPoint( local );
		var hoverHandle = HitHandle( local );

		if ( hover == _hoverIndex && hoverHandle == _hoverHandle )
			return;

		_hoverIndex = hover;
		_hoverHandle = hoverHandle;
		SetClass( "over-item", hover >= 0 || hoverHandle );
		Repaint();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		_draggingHandle = false;
		EndPointDrag();
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Pressed && e.Button == "delete" && Selected >= 0 )
		{
			RemovePoint( Selected );
			e.StopPropagation = true;
			return;
		}

		base.OnButtonTyped( e );
	}

	private Vector2 Snap( Vector2 p, bool snap )
	{
		if ( !snap )
			return p;

		var step = _gridStep * 0.25f;
		return new Vector2( MathF.Round( p.x / step ) * step, MathF.Round( p.y / step ) * step );
	}

	private void OpenContextMenu( Vector2 local )
	{
		var menu = new Menu();
		var hit = HitPoint( local );

		if ( hit >= 0 )
		{
			Select( hit );
			menu.AddOption( "Edit Point", "grain", () => Editor.NavigateInto( SelectedPoint.Graph ) );
			menu.AddSeparator();
			var remove = menu.AddOption( "Remove Point", "delete", () => RemovePoint( hit ) );
			remove.Enabled = _node.Points.Count > 3;
		}
		else
		{
			var position = Snap( FromCanvas( local ), true );
			menu.AddOption( "Add Point Here", "add", () => AddPoint( position ) );
		}

		menu.AddSeparator();
		menu.AddOption( "Fit View", "fit_screen", Fit );

		menu.OpenAtCursor( this );
	}

	protected override void PaintCanvas()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( Paint.LocalRect );

		PaintGrid();
		PaintBlendSpace();
		PaintHint();
	}

	private void PaintHint()
	{
		Paint.SetPen( Color.White.WithAlpha( 0.3f ) );
		Paint.SetDefaultFont( 7 );
		Paint.DrawText( Paint.LocalRect.Shrink( 0, 4, 0, 0 ),
			"Double-click to add a point  ·  Drag to move  ·  Ctrl snaps to grid  ·  Right-click for options",
			TextFlag.CenterTop );
	}

	private void PaintGrid()
	{
		//Pick a grid step that keeps lines a comfortable distance apart
		_gridStep = 1;
		while ( _gridStep * Zoom < 40 ) _gridStep *= 2;
		while ( _gridStep * Zoom > 100 ) _gridStep *= 0.5f;

		var min = FromCanvas( new Vector2( 0, Height ) );
		var max = FromCanvas( new Vector2( Width, 0 ) );

		Paint.ClearBrush();
		Paint.SetDefaultFont( 7 );

		for ( var x = MathF.Floor( min.x / _gridStep ) * _gridStep; x <= max.x; x += _gridStep )
		{
			var sx = ToCanvas( new Vector2( x, 0 ) ).x;
			var zero = MathF.Abs( x ) < _gridStep * 0.5f;

			Paint.SetPen( zero ? Theme.WindowBackground.Lighten( 0.7f ) : Theme.WindowBackground.Lighten( 0.3f ) );
			Paint.DrawLine( new Vector2( sx, 0 ), new Vector2( sx, Height ) );

			Paint.SetPen( Color.White.WithAlpha( zero ? 0.4f : 0.25f ) );
			Paint.DrawText( new Rect( sx - 20, Height - 16, 40, 12 ), x.ToString( "0.##" ) );
		}

		for ( var y = MathF.Floor( min.y / _gridStep ) * _gridStep; y <= max.y; y += _gridStep )
		{
			var sy = ToCanvas( new Vector2( 0, y ) ).y;
			var zero = MathF.Abs( y ) < _gridStep * 0.5f;

			Paint.SetPen( zero ? Theme.WindowBackground.Lighten( 0.7f ) : Theme.WindowBackground.Lighten( 0.3f ) );
			Paint.DrawLine( new Vector2( 0, sy ), new Vector2( Width, sy ) );

			Paint.SetPen( Color.White.WithAlpha( zero ? 0.4f : 0.25f ) );
			Paint.DrawText( new Rect( 4, sy - 6, 40, 12 ), y.ToString( "0.##" ), TextFlag.LeftCenter );
		}
	}

	private void PaintBlendSpace()
	{
		var points = _node.Points;
		if ( points.Count < 3 )
			return;

		var positions = points.Select( p => p.Position ).ToList();
		var valid = BlendSpace.Triangulate( positions, out var triangles, out var hull );
		var primary = valid ? Theme.Green : Theme.Red;
		var weights = new float[points.Count];

		if ( valid )
		{
			weights = BlendSpace.CalculateWeights( positions, triangles, hull, _blendValue, out _ );

			Paint.ClearBrush();
			Paint.SetPen( primary.WithAlpha( 0.25f ) );

			for ( var i = 0; i < triangles.Count; i += 3 )
			{
				var a = ToCanvas( points[triangles[i]].Position );
				var b = ToCanvas( points[triangles[i + 1]].Position );
				var c = ToCanvas( points[triangles[i + 2]].Position );

				Paint.DrawLine( a, b );
				Paint.DrawLine( b, c );
				Paint.DrawLine( c, a );
			}

			Paint.SetPen( primary.WithAlpha( 0.7f ), 2 );
			for ( var i = 1; i < hull.Count; i++ )
				Paint.DrawLine( ToCanvas( points[hull[i - 1]].Position ), ToCanvas( points[hull[i]].Position ) );
		}
		else
		{
			Paint.SetPen( Theme.Red );
			Paint.SetDefaultFont( 8, 500 );
			Paint.DrawText( Paint.LocalRect, "Invalid Blend Space — points can't be duplicated or collinear" );
		}

		for ( var i = 0; i < points.Count; i++ )
			PaintPoint( ToCanvas( points[i].Position ), _node.PointTitle( i ), primary, i == Selected, i == _hoverIndex, weights[i] );

		if ( valid )
			PaintHandle( ToCanvas( _blendValue ) );
	}

	private void PaintPoint( Vector2 screen, string title, Color primary, bool selected, bool hovered, float weight )
	{
		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.3f ) );
		Paint.DrawCircle( screen + new Vector2( 0, 2 ), 18 );

		//Blend weight ring - grows with this point's contribution
		if ( weight > 0.001f )
		{
			Paint.SetBrush( primary.WithAlpha( 0.15f + weight * 0.2f ) );
			Paint.DrawCircle( screen, 18 + weight * 14 );

			Paint.ClearBrush();
			Paint.SetPen( primary.WithAlpha( 0.4f + weight * 0.6f ), 2 );
			Paint.DrawCircle( screen, 18 + weight * 14 );
			Paint.ClearPen();
		}

		Paint.SetBrush( primary.Darken( hovered || selected ? 0.4f : 0.55f ) );
		Paint.DrawCircle( screen, 18 );

		Paint.ClearBrush();
		Paint.SetPen( selected ? SelectionColor : primary.WithAlpha( hovered ? 0.9f : 0.5f ), selected ? 2 : 1.2f );
		Paint.DrawCircle( screen, 18 );

		Paint.ClearPen();
		Paint.SetBrush( selected ? SelectionColor : primary.Lighten( 0.5f ) );
		Paint.DrawCircle( screen, 7 );

		Paint.SetDefaultFont( 8, 500 );
		Paint.SetPen( Theme.Text.WithAlpha( hovered || selected ? 1f : 0.75f ) );
		Paint.DrawText( new Rect( screen + new Vector2( -60, 16 ), new Vector2( 120, 16 ) ), title );
	}

	private void PaintHandle( Vector2 screen )
	{
		var color = Theme.Red.Lighten( 0.2f );

		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.3f ) );
		Paint.DrawCircle( screen + new Vector2( 0, 1 ), 8 );

		Paint.SetBrush( _draggingHandle ? color.Darken( 0.2f ) : color );
		Paint.DrawCircle( screen, _hoverHandle || _draggingHandle ? 8 : 7 );

		Paint.ClearBrush();
		Paint.SetPen( Color.White.WithAlpha( 0.9f ), 1.5f );
		Paint.DrawCircle( screen, _hoverHandle || _draggingHandle ? 8 : 7 );
	}
}

//Isolated preview beside the blend space canvas - plays just this blend space as a tiny compiled
//graph, scrubbed by the canvas blend handle. Independent of the graph preview and its parameters.
internal sealed class BlendSpacePreview : PreviewScenePanel
{
	private readonly Window _window;
	private Blend2DNode _node;

	private bool _dirty = true;
	private RealTimeSince _sinceDirty;
	private Vector2 _blendValue;
	private readonly UILabel _hint;
	private AnimGraph _previewGraph;

	public BlendSpacePreview( Blend2DNode node, Window window ) : base( "Blend Space Preview" )
	{
		_window = window;
		_node = node;

		AddClass( "blend-space-preview" );

		_hint = AddChild<UILabel>( "hint hidden" );
		_hint.Tokenize = false;
		_hint.Text = "Connect an animation clip to every point to preview";
	}

	public void Rebind( Blend2DNode node )
	{
		_node = node;
		MarkDirty();
	}

	public void MarkDirty()
	{
		_dirty = true;
		_sinceDirty = 0;
	}

	public void SetBlendValue( Vector2 value )
	{
		_blendValue = value;
		ApplyBlendValue();
	}

	private void ApplyBlendValue()
	{
		if ( !Renderer.IsValid() )
			return;

		Renderer.Set( "x", _blendValue.x );
		Renderer.Set( "y", _blendValue.y );
	}

	private void Rebuild()
	{
		_dirty = false;

		SetModel( _window.Graph?.TargetModel );

		ClearPreviewGraph();

		if ( Renderer is null )
			return;

		//Every point needs a clip - the blend space previews in isolation
		var clips = _node.Points
			.Select( point =>
			{
				var output = point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault();
				return (point.Position, Clip: output?.GetConnectedNode( output.Pose ) as AnimationClipNode);
			} )
			.Where( e => !string.IsNullOrWhiteSpace( e.Clip?.AnimationName ) )
			.ToList();

		if ( clips.Count < 3 || clips.Count != _node.Points.Count )
		{
			_hint.SetClass( "hidden", false );
			Renderer.UseAnimGraph = false;
			return;
		}

		_hint.SetClass( "hidden", true );

		var builder = AnimGraph.Builder;
		builder.Skeleton = _window.Graph?.TargetSkeleton;
		var blend = builder.BlendSpace2D( builder.FloatParameter( "x" ), builder.FloatParameter( "y" ) );

		foreach ( var (position, clip) in clips )
			blend.Add( position, builder.Clip( clip.AnimationName, new AnimGraphBuilder.ClipSettings { Looping = clip.Loop } ) );

		_previewGraph = builder.Create( blend, "blendspace_preview" );

		Renderer.UseAnimGraph = _previewGraph is not null;
		Renderer.AnimGraph = _previewGraph;

		ApplyBlendValue();
	}

	//The preview graph is built here rather than loaded, so nothing else can be holding it and each rebuild has to hand the old one back.
	private void ClearPreviewGraph()
	{
		if ( _previewGraph is null )
			return;

		if ( Renderer.IsValid() )
			Renderer.AnimGraph = null;

		_previewGraph.Dispose();
		_previewGraph = null;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		ClearPreviewGraph();
	}

	protected override void OnScenePreFrame()
	{
		if ( _dirty && _sinceDirty > 0.2f )
			Rebuild();

		ApplyPreviewSettings( _window.Graph );
	}
}
