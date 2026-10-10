namespace UnitTests;

//A hull loaded from a model carries no edges, so its outline is worked out from its corners.
[TestClass]
public class HullOutlineTest
{
	static Rinten.PhysicsGroupDescription.BodyPart.HullPart Hull( params Vector3[] points )
		=> new( points, System.Array.Empty<int>(), null );

	[TestMethod]
	public void ACubeIsDrawnAsTwelveEdges()
	{
		var lines = Hull(
			new Vector3( -1, -1, -1 ), new Vector3( 1, -1, -1 ),
			new Vector3( -1, 1, -1 ), new Vector3( 1, 1, -1 ),
			new Vector3( -1, -1, 1 ), new Vector3( 1, -1, 1 ),
			new Vector3( -1, 1, 1 ), new Vector3( 1, 1, 1 ) ).GetLines().ToList();

		//Not eighteen: a square face is cut into two triangles, and that seam is not a corner.
		Assert.AreEqual( 12, lines.Count, "a box has twelve edges" );

		foreach ( var line in lines )
		{
			Assert.AreEqual( 2.0f, ( line.End - line.Start ).Length, 0.001f,
				"every edge of a cube runs along one side of it" );
		}
	}

	[TestMethod]
	public void ATetrahedronIsDrawnAsSixEdges()
	{
		var lines = Hull(
			new Vector3( 0, 0, 0 ), new Vector3( 1, 0, 0 ),
			new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 ) ).GetLines().ToList();

		Assert.AreEqual( 6, lines.Count, "four corners, every pair of them an edge" );
	}

	[TestMethod]
	public void PointsInsideTheShapeAreNotDrawn()
	{
		var lines = Hull(
			new Vector3( -1, -1, -1 ), new Vector3( 1, -1, -1 ),
			new Vector3( -1, 1, -1 ), new Vector3( 1, 1, -1 ),
			new Vector3( -1, -1, 1 ), new Vector3( 1, -1, 1 ),
			new Vector3( -1, 1, 1 ), new Vector3( 1, 1, 1 ),
			new Vector3( 0, 0, 0 ), new Vector3( 0.2f, -0.1f, 0.4f ) ).GetLines().ToList();

		Assert.AreEqual( 12, lines.Count, "a point in the middle is not a corner of anything" );
	}

	[TestMethod]
	public void EdgesThatCameWithTheShapeAreUsedAsTheyAre()
	{
		var part = new Rinten.PhysicsGroupDescription.BodyPart.HullPart(
			[new Vector3( 0, 0, 0 ), new Vector3( 1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 )],
			[0, 1], null );

		Assert.AreEqual( 1, part.GetLines().Count(), "nothing is worked out for a hull that brought its own" );
	}

	[TestMethod]
	public void AShapeWithNoVolumeIsDrawnAsNothing()
	{
		var lines = Hull(
			new Vector3( 0, 0, 0 ), new Vector3( 1, 0, 0 ),
			new Vector3( 1, 1, 0 ), new Vector3( 0, 1, 0 ) ).GetLines().ToList();

		Assert.AreEqual( 0, lines.Count, "a flat square is not a hull, and guessing at one draws rubbish" );
	}
}
