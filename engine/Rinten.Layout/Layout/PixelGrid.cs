using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Snaps a finished layout to whole pixels; off in the UI (scale 0), done in double to avoid drift
internal static class PixelGrid
{
	public static float RoundValueToPixelGrid( double value, double pointScaleFactor, bool forceCeil, bool forceFloor )
	{
		var scaled = value * pointScaleFactor;

		//The fraction to subtract to reach the floor. For a negative number the remainder comes back negative
		//- -2.2 gives -0.2 - and one has to be added to it, or the "floor" would round the wrong way.
		var fractial = scaled - Math.Truncate( scaled );

		if ( fractial < 0 ) ++fractial;

		if ( InexactEquals( fractial, 0 ) )
		{
			scaled -= fractial;
		}
		else if ( InexactEquals( fractial, 1.0 ) )
		{
			scaled = scaled - fractial + 1.0;
		}
		else if ( forceCeil )
		{
			scaled = scaled - fractial + 1.0;
		}
		else if ( forceFloor )
		{
			scaled -= fractial;
		}
		else
		{
			scaled = scaled - fractial +
				( !double.IsNaN( fractial ) && ( fractial > 0.5 || InexactEquals( fractial, 0.5 ) ) ? 1.0 : 0.0 );
		}

		return double.IsNaN( scaled ) || double.IsNaN( pointScaleFactor )
			? Num.Undefined
			: (float)( scaled / pointScaleFactor );
	}

	/// <summary>Rounds a node and everything under it, in place.</summary>
	public static void RoundLayoutResults( LayoutNode node, double absoluteLeft, double absoluteTop )
	{
		double pointScaleFactor = node.Config.PointScaleFactor;

		double nodeLeft = node.Layout.Position( PhysicalEdge.Left );
		double nodeTop = node.Layout.Position( PhysicalEdge.Top );

		double nodeWidth = node.Layout.GetDimension( Dimension.Width );
		double nodeHeight = node.Layout.GetDimension( Dimension.Height );

		var absoluteNodeLeft = absoluteLeft + nodeLeft;
		var absoluteNodeTop = absoluteTop + nodeTop;

		var absoluteNodeRight = absoluteNodeLeft + nodeWidth;
		var absoluteNodeBottom = absoluteNodeTop + nodeHeight;

		if ( pointScaleFactor != 0.0 )
		{
			//A node that measures itself is never rounded down: a text run a fraction narrower than it asked for loses a word.
			var textRounding = node.NodeType == NodeType.Text;

			node.Layout.SetPosition( PhysicalEdge.Left,
				RoundValueToPixelGrid( nodeLeft, pointScaleFactor, false, textRounding ) );

			node.Layout.SetPosition( PhysicalEdge.Top,
				RoundValueToPixelGrid( nodeTop, pointScaleFactor, false, textRounding ) );

			//Whether the size lands on a whole pixel decides which way a text node is allowed to round.
			var scaledWidth = nodeWidth * pointScaleFactor;
			var hasFractionalWidth = !InexactEquals( Math.Round( scaledWidth, MidpointRounding.AwayFromZero ), scaledWidth );

			var scaledHeight = nodeHeight * pointScaleFactor;
			var hasFractionalHeight = !InexactEquals( Math.Round( scaledHeight, MidpointRounding.AwayFromZero ), scaledHeight );

			node.Layout.SetDimension( Dimension.Width,
				RoundValueToPixelGrid( absoluteNodeRight, pointScaleFactor,
					textRounding && hasFractionalWidth, textRounding && !hasFractionalWidth )
				- RoundValueToPixelGrid( absoluteNodeLeft, pointScaleFactor, false, textRounding ) );

			node.Layout.SetDimension( Dimension.Height,
				RoundValueToPixelGrid( absoluteNodeBottom, pointScaleFactor,
					textRounding && hasFractionalHeight, textRounding && !hasFractionalHeight )
				- RoundValueToPixelGrid( absoluteNodeTop, pointScaleFactor, false, textRounding ) );
		}

		foreach ( var child in node.RawChildren )
			RoundLayoutResults( child, absoluteNodeLeft, absoluteNodeTop );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool InexactEquals( double a, double b )
	{
		if ( !double.IsNaN( a ) && !double.IsNaN( b ) ) return Math.Abs( a - b ) < 0.0001;

		return double.IsNaN( a ) && double.IsNaN( b );
	}
}
