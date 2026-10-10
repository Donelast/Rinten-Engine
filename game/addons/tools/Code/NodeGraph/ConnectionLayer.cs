using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>Every wire in the graph, drawn on one panel under the nodes.</summary>
public sealed class ConnectionLayer : Panel
{
	private readonly GraphView _graph;

	public ConnectionLayer( GraphView graph )
	{
		_graph = graph;

		//The wires answer for themselves where the pointer is concerned - the view asks each of them whether it
		//is on the line. A panel this size taking clicks would take every one meant for the canvas behind it.
		AddClass( "connection-layer" );
	}

	//The graph marks this layer whenever a wire moves, changes colour or the view moves -
	//see GraphView.RedrawConnections - so the wires are kept, not drawn again every frame.
	protected override bool RetainsDrawing => true;

	//The line moved into drawing space, kept between wires so each does not allocate one.
	private readonly List<Vector2> _scratch = new();

	public override void OnDraw()
	{
		var origin = Box.Rect.Position;
		var showing = _graph.Viewport.SceneRect.Grow( 64.0f );

		//Behind, then the ones being pointed at, so a wire somebody is looking at is not buried under the dozen it crosses.
		foreach ( var connection in _graph.DrawOrderedConnections )
		{
			if ( !connection.IsValid() ) continue;
			if ( connection.Path.Count < 2 ) continue;
			if ( !connection.SceneBounds.IsInside( showing, false ) ) continue;

			Offset( connection.Path.Points, origin );

			//One stroke through the whole wire, round at its ends and its bends: its lengths
			//meet without overlapping, so a translucent wire is not darker at every joint.
			Draw.Polyline( _scratch, connection.DrawWidth, connection.DrawColor, StrokeLinecap.Round, StrokeLinejoin.Round );
		}
	}

	//Moves the line into the space the drawing is done in, which is this panel's box before the world's transform is put on it.
	private void Offset( IReadOnlyList<Vector2> points, Vector2 origin )
	{
		_scratch.Clear();

		for ( var i = 0; i < points.Count; i++ )
		{
			_scratch.Add( origin + points[i] );
		}
	}
}
