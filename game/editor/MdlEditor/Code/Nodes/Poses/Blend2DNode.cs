using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Blends synchronized poses across a triangulated two-dimensional parameter space. <br/><br/><b>Use when:</b>&#160;Blending directional movement or other poses driven by two values. <br/><b>Important:</b>&#160;Requires at least three unique, non-collinear points. Failed triangulation outputs the reference pose.</summary>
[Title( "Blend 2D" ), Group( "Blend" ), Icon( "grain" )]
public partial class Blend2DNode : Node
{
	public sealed class BlendPoint
	{
		[Property]
		public string Name { get; set; } = "";

		[Property]
		public Vector2 Position { get; set; }

		/// <summary>Pose graph sampled at this position.</summary>
		[Hide]
		public Data Graph { get; set; } = Data.CreateSubGraph<PoseResultNode>();

		[Hide]
		public NodeInput Input { get; set; }

		//Not the legacy Input above, which is migrated into the point's own graph on load and cleared. This one is the drawn shape an imported graph arrives in and stays wired.
		/// <summary>A pose wired into this point from the graph the blend sits in, used instead of <see cref="Graph"/> when it is connected.</summary>
		[Hide]
		public NodeInput Pose { get; set; }
	}

	[Input( typeof( FloatLink ) )]
	public NodeInput X { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Y { get; set; }

	[Property, Hide]
	public List<BlendPoint> Points { get; set; } =
	[
		new() { Position = new Vector2( 0, 1 ) },
		new() { Position = new Vector2( -1, 0 ) },
		new() { Position = new Vector2( 1, 0 ) },
	];

	[Property]
	public bool Loop { get; set; } = true;

	/// <summary>Whether the poses this blends are played as one stride rather than each on its own clock.</summary>
	[Property]
	public bool Synchronized { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Blue;

	private readonly List<IPlugIn> _pointPlugs = new();

	//One pose pin per point, for poses drawn beside the blend.
	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			while ( _pointPlugs.Count > Points.Count )
				_pointPlugs.RemoveAt( _pointPlugs.Count - 1 );

			while ( _pointPlugs.Count < Points.Count )
			{
				var i = _pointPlugs.Count;

				var info = new AnimPlugInfo( $"Point{i}",
					() => i < Points.Count ? PointTitle( i ) : $"Point {i + 1}",
					_ => i < Points.Count ? Points[i].Pose : default,
					( _, value ) => { if ( i < Points.Count ) Points[i].Pose = value; } );

				_pointPlugs.Add( new AnimPlugIn( this, info, typeof( PoseLink ) ) );
			}

			return _pointPlugs;
		}
	}

	private const float PreviewSize = 240;
	private static readonly Color LiveBlendColor = new( 1.0f, 0.84f, 0.0f );

	[JsonIgnore]
	internal bool HasLiveBlendDebug { get; private set; }

	[JsonIgnore]
	internal Vector2 LiveBlendParameter { get; private set; }

	public override IEnumerable<Data> SubGraphs
	{
		get
		{
			foreach ( var point in Points )
				yield return point.Graph;
		}
	}

	internal void InitializePointGraphs()
	{
		foreach ( var point in Points )
		{
			point.Graph ??= Data.CreateSubGraph<PoseResultNode>();
			point.Graph.SetRoot( Root );
		}
	}

	internal BlendPoint AddPoint( Vector2 position )
	{
		var point = new BlendPoint { Position = position };
		point.Graph.SetRoot( Root );
		Points.Add( point );
		Update();
		return point;
	}

	internal void RemovePoint( BlendPoint point )
	{
		if ( point is null || Points.Count <= 3 )
			return;

		Points.Remove( point );
		Update();
	}

	public string PointTitle( int i )
	{
		if ( i < 0 || i >= Points.Count )
			return $"Point {i + 1}";

		var point = Points[i];
		if ( !string.IsNullOrWhiteSpace( point.Name ) )
			return point.Name;

		if ( point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is { } output
			&& output.GetConnectedNode( output.Pose ) is { } source )
			return source.DisplayInfo.Name;

		return $"Point {i + 1}";
	}

	internal override void BuildDebugWidgets( Rinten.UI.Panel parent )
	{
		parent.AddChild( new PreviewCanvas( this ) );
		base.BuildDebugWidgets( parent );
	}

	private sealed class PreviewCanvas( Blend2DNode node ) : LiveCanvas( PreviewSize, square: true )
	{
		protected override void OnPaintContent( Rect rect ) => node.PaintDebugPreview( rect );
	}

	private void PaintDebugPreview( Rect area )
	{
		Paint.Antialiasing = true;

		//The widget caps its height, so on wide docks center a square within the rect.
		var side = MathF.Min( area.Width, area.Height );
		area = new Rect( area.Left + (area.Width - side) * 0.5f, area.Top, side, side );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( area, 4 );

		if ( Points.Count < 3 )
			return;

		var positions = Points.Select( p => p.Position ).ToList();
		var valid = BlendSpace.Triangulate( positions, out var triangles, out var hull );
		var color = valid ? Theme.Green : Theme.Red;

		//Map blend space positions into the preview rect with some padding
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );

		foreach ( var point in Points )
		{
			min = Vector2.Min( min, point.Position );
			max = Vector2.Max( max, point.Position );
		}

		var size = Vector2.Max( max - min, new Vector2( 0.001f ) );
		var inner = area.Shrink( 20 );

		Vector2 ToScreen( Vector2 p ) => new(
			inner.Left + (p.x - min.x) / size.x * inner.Width,
			inner.Top + (1.0f - (p.y - min.y) / size.y) * inner.Height );

		if ( valid )
		{
			Paint.ClearBrush();
			Paint.SetPen( color.WithAlpha( 0.3f ) );

			for ( var i = 0; i < triangles.Count; i += 3 )
			{
				var a = ToScreen( Points[triangles[i]].Position );
				var b = ToScreen( Points[triangles[i + 1]].Position );
				var c = ToScreen( Points[triangles[i + 2]].Position );

				Paint.DrawLine( a, b );
				Paint.DrawLine( b, c );
				Paint.DrawLine( c, a );
			}

			Paint.SetPen( color.WithAlpha( 0.8f ) );
			for ( var i = 1; i < hull.Count; i++ )
				Paint.DrawLine( ToScreen( Points[hull[i - 1]].Position ), ToScreen( Points[hull[i]].Position ) );
		}
		else
		{
			Paint.SetPen( Theme.Red );
			Paint.SetDefaultFont( 7 );
			Paint.DrawText( area, "Invalid Blend Space" );
		}

		var weights = valid && HasLiveBlendDebug
			? BlendSpace.CalculateWeights( positions, triangles, hull, LiveBlendParameter, out _ )
			: null;

		for ( var i = 0; i < Points.Count; i++ )
		{
			var screen = ToScreen( Points[i].Position );
			var weight = weights is null ? 0.0f : weights[i];

			if ( weight > 0.001f )
			{
				Paint.ClearPen();
				Paint.SetBrush( LiveBlendColor.WithAlpha( 0.15f + weight * 0.35f ) );
				Paint.DrawCircle( screen, 6.0f + weight * 10.0f );
			}

			Paint.ClearPen();
			Paint.SetBrush( weight > 0.001f ? Color.Lerp( color, LiveBlendColor, weight ) : color );
			Paint.DrawCircle( screen, 4.0f + weight * 3.0f );
		}

		if ( HasLiveBlendDebug )
		{
			var marker = ToScreen( LiveBlendParameter );
			Paint.SetPen( Theme.WidgetBackground, 2 );
			Paint.SetBrush( LiveBlendColor );
			Paint.DrawCircle( marker, 6 );

			var text = string.Join( "\n", LiveContributions().Select( c => $"{PointTitle( c.Point )}  {c.Weight * 100.0f:0}%" ) );
			DebugMarker.PaintReadout( area, marker, text );
		}
	}

	private List<(int Point, float Weight)> LiveContributions()
	{
		if ( !HasLiveBlendDebug || Points.Count < 3 )
			return [];

		var positions = Points.Select( p => p.Position ).ToList();
		if ( !BlendSpace.Triangulate( positions, out var triangles, out var hull ) )
			return [];

		var weights = BlendSpace.CalculateWeights( positions, triangles, hull, LiveBlendParameter, out _ );

		return Enumerable.Range( 0, Points.Count )
			.Where( i => weights[i] > 0.001f )
			.Select( i => (i, weights[i]) )
			.OrderByDescending( c => c.Item2 )
			.ToList();
	}

	public bool SetLiveBlendDebug( bool hasDebug, Vector2 parameter = default )
	{
		if ( HasLiveBlendDebug == hasDebug && LiveBlendParameter == parameter )
			return false;

		HasLiveBlendDebug = hasDebug;
		LiveBlendParameter = parameter;
		return true;
	}

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		if ( Points.Count < 3 )
		{
			compiler.Warn( "Blend 2D needs at least 3 points" );
			return compiler.ReferencePose();
		}

		if ( !BlendSpace.Triangulate( Points.Select( p => p.Position ).ToList(), out var triangles, out var hull ) )
		{
			compiler.Warn( "Blend 2D triangulation failed - points can't be duplicated or collinear" );
			return compiler.ReferencePose();
		}

		var sources = Points.Select( ( point, index ) => EmitPoint( compiler, point, index ) ).ToList();
		var x = compiler.EmitValue( this, X );
		if ( x is null )
			x = compiler.ConstFloat( 0 );
		var y = compiler.EmitValue( this, Y );
		if ( y is null )
			y = compiler.ConstFloat( 0 );

		return compiler.Builder.BlendSpace2D(
			x,
			y,
			new AnimGraphBuilder.BlendSpace2DSettings
			{
				Sources = sources,
				Positions = Points.Select( point => point.Position ).ToList(),
				Indices = triangles,
				HullIndices = hull,
				Looping = Loop,
				Synchronized = Synchronized,
			} );
	}

	//What a point samples: whatever is wired into its pin, and failing that the pose graph the point owns.
	private AnimGraphBuilder.Node EmitPoint( Compiler compiler, BlendPoint point, int index )
	{
		var name = string.IsNullOrWhiteSpace( point.Name ) ? $"Blend 2D point {index + 1}" : $"Blend 2D point '{point.Name}'";

		return point.Pose.IsValid
			? compiler.EmitPose( this, point.Pose, name, synchronized: true )
			: EmitPointGraph( compiler, point, index );
	}

	private static AnimGraphBuilder.Node EmitPointGraph( Compiler compiler, BlendPoint point, int index )
	{
		var title = string.IsNullOrWhiteSpace( point.Name ) ? $"Blend 2D point {index + 1}" : $"Blend 2D point '{point.Name}'";

		if ( point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is not { } output )
		{
			compiler.Warn( $"{title} has no output node" );
			return compiler.ReferencePose();
		}

		return compiler.EmitPose( output, output.Pose, title, synchronized: true );
	}
}
