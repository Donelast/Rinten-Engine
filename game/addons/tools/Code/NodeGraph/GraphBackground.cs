using Rinten.UI;

namespace Editor.NodeEditor;

//A child of the canvas rather than of the world, so it draws in view space and the lines stay one pixel wide however close the view is zoomed. What the zoom changes is how far apart they are.
/// <summary>The ruled paper the graph is drawn on.</summary>
public sealed class GraphBackground : Panel
{
	//Below this the lines are closer together than they are wide, so there is no grid to see.
	private const float FadeSpacing = 6.0f;

	private readonly GraphView _graph;

	public GraphBackground( GraphView graph )
	{
		_graph = graph;

		AddClass( "graph-background" );
	}

	public override void OnDraw()
	{
		var spacing = _graph.GridSize * _graph.Viewport.Zoom;

		if ( spacing < FadeSpacing ) return;

		var rect = Box.Rect;
		var offset = _graph.Viewport.Offset;
		var colour = Theme.WindowBackground.Lighten( 0.3f );

		for ( var x = rect.Left + Wrap( offset.x, spacing ); x <= rect.Right; x += spacing )
		{
			Draw.Rect( new Rect( x, rect.Top, 1.0f, rect.Height ), colour );
		}

		for ( var y = rect.Top + Wrap( offset.y, spacing ); y <= rect.Bottom; y += spacing )
		{
			Draw.Rect( new Rect( rect.Left, y, rect.Width, 1.0f ), colour );
		}
	}

	//Where the first line falls inside the box, given where the graph's origin is.
	private static float Wrap( float offset, float spacing )
	{
		var at = offset % spacing;

		return at < 0.0f ? at + spacing : at;
	}
}
