using Editor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EditorTests;

//Graph wires are hit tested by their curve, not their mostly-empty bounding rect
[TestClass]
public class GraphHitTestTests
{
	//A wire straight across, as a curve is by the time it is drawn.
	private static GraphicsLine Across()
	{
		var line = new GraphicsLine();

		line.MoveTo( new Vector2( 0, 0 ) );
		line.LineTo( new Vector2( 400, 0 ) );

		return line;
	}

	[TestMethod]
	public void ThePointerIsOnAWireOnlyNearIt()
	{
		var line = Across();

		Assert.IsTrue( line.Contains( new Vector2( 200, 0 ) ), "the middle of the wire is on it" );
		Assert.IsTrue( line.Contains( new Vector2( 0, 0 ) ), "so is either end" );
		Assert.IsTrue( line.Contains( new Vector2( 400, 0 ) ) );

		//Inside the box the wire spans, and nowhere near the wire.
		Assert.IsFalse( line.Contains( new Vector2( 200, 120 ) ),
			"a point in the empty half of a wire's box is not on the wire" );
		Assert.IsFalse( line.Contains( new Vector2( 200, -120 ) ) );

		//Past the ends, which the box does not even reach.
		Assert.IsFalse( line.Contains( new Vector2( -200, 0 ) ) );
		Assert.IsFalse( line.Contains( new Vector2( 600, 0 ) ) );
	}

	[TestMethod]
	public void AWireIsGrabbableAWayOutFromItsCentre()
	{
		var line = Across();
		line.HitWidth = 4.0f;

		//The wire is drawn four wide, so its own width counts as on it.
		Assert.IsTrue( line.Contains( new Vector2( 200, 2 ) ) );
		Assert.IsTrue( line.Contains( new Vector2( 200, -2 ) ) );

		//And a hand's width past that is not, or the fix would be no fix.
		Assert.IsFalse( line.Contains( new Vector2( 200, 32 ) ) );
	}

	[TestMethod]
	public void ACurveIsFollowedRatherThanBoxed()
	{
		var line = new GraphicsLine();

		//The shape a connection takes: out of one plug, across, into another two hundred below.
		line.MoveTo( new Vector2( 0, 0 ) );
		line.CubicLineTo( new Vector2( 150, 0 ), new Vector2( 150, 200 ), new Vector2( 300, 200 ) );

		Assert.IsTrue( line.Contains( new Vector2( 150, 100 ) ), "the middle of an S bend is on it" );

		//The two corners of the box the bend spans, which the bend goes nowhere near.
		Assert.IsFalse( line.Contains( new Vector2( 290, 10 ) ) );
		Assert.IsFalse( line.Contains( new Vector2( 10, 190 ) ) );
	}

	//An item that says nothing about its shape, which is most of them.
	private sealed class PlainItem : GraphicsItem
	{
	}

	[TestMethod]
	public void AnItemWithNoShapeOfItsOwnIsStillAllOfItsBox()
	{
		//The default answer, because most items do fill their rect - narrowing the hit test must not have narrowed those.
		var item = new PlainItem { Size = new Vector2( 100, 50 ) };

		Assert.IsTrue( item.Contains( new Vector2( 50, 25 ) ) );
		Assert.IsTrue( item.Contains( new Vector2( 0, 0 ) ) );
	}
}
