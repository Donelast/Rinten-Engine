using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>The box dragged across the canvas to catch what it covers.</summary>
public sealed class MarqueeItem : GraphItem
{
	public MarqueeItem( GraphView graph ) : base( graph )
	{
		//It is drawn over the graph and is not part of it: anything it took the pointer for would be something the drag it belongs to could not reach.
		AddClass( "marquee" );

		ZIndex = 100.0f;
	}
}
