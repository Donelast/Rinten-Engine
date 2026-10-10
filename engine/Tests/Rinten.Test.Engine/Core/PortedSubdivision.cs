using System.Collections.Generic;
using Rinten;
using Rinten.Porting.SBox;

namespace EngineTests;

//Map displacements (subdivided faces) are rebuilt from the control cage on import
[TestClass]
[DoNotParallelize]
public class PortedSubdivisionTests
{
	//A flat grid of quads, w by h of them, in the z = 0 plane.
	private static (List<Vector3> Points, List<int[]> Faces) Grid( int w, int h )
	{
		var points = new List<Vector3>();

		for ( var y = 0; y <= h; y++ )
		{
			for ( var x = 0; x <= w; x++ ) points.Add( new Vector3( x, y, 0 ) );
		}

		var faces = new List<int[]>();

		for ( var y = 0; y < h; y++ )
		{
			for ( var x = 0; x < w; x++ )
			{
				var at = ( y * ( w + 1 ) ) + x;

				faces.Add( new[] { at, at + 1, at + w + 2, at + w + 1 } );
			}
		}

		return (points, faces);
	}

	//A face marked to be subdivided becomes four, and the one beside it that was not marked stays as it was.
	[TestMethod]
	public void OnlyTheMarkedFaceIsSubdivided()
	{
		var (points, faces) = Grid( 2, 1 );

		var cages = new[]
		{
			new SBoxSubdivision.Cage( faces[0], 1 ),
			new SBoxSubdivision.Cage( faces[1], 0 ),
		};

		var made = SBoxSubdivision.Apply( points, cages ).Faces;

		Assert.AreEqual( 4, made[0].Count, "one level is four quads" );
		Assert.AreEqual( 1, made[1].Count, "and the face beside it is untouched" );

		foreach ( var quad in made[0] ) Assert.AreEqual( 4, quad.Length, "Catmull-Clark makes quads" );
	}

	//The border a patch shares with what it is welded into does not move. The whole reason the unmarked faces are read at all. A displacement is cut
	//into a wall or a floor; if smoothing were allowed to pull its edge inward the level would come apart along a seam nobody would think to look at.
	[TestMethod]
	public void TheBorderItIsWeldedIntoStaysWhereItIs()
	{
		var (points, faces) = Grid( 2, 1 );

		//The two vertices the two faces share.
		var shared = new[] { 1, 4 };
		var before = new[] { points[shared[0]], points[shared[1]] };

		var cages = new[]
		{
			new SBoxSubdivision.Cage( faces[0], 2 ),
			new SBoxSubdivision.Cage( faces[1], 0 ),
		};

		SBoxSubdivision.Apply( points, cages );

		for ( var i = 0; i < shared.Length; i++ )
		{
			Assert.AreEqual( before[i].x, points[shared[i]].x, 0.0001f, $"corner {i} x" );
			Assert.AreEqual( before[i].y, points[shared[i]].y, 0.0001f, $"corner {i} y" );
			Assert.AreEqual( before[i].z, points[shared[i]].z, 0.0001f, $"corner {i} z" );
		}
	}

	//Subdivision cuts faces without moving the cage, unlike Catmull-Clark smoothing
	[TestMethod]
	public void ItCutsWithoutMovingTheCage()
	{
		var (points, faces) = Grid( 3, 3 );

		//One vertex pulled up out of the plane, with faces all round it: the crudest possible displacement.
		const int raised = ( 1 * 4 ) + 1;
		points[raised] = new Vector3( 1, 1, 4 );

		var before = new List<Vector3>( points );

		var cages = new List<SBoxSubdivision.Cage>();
		foreach ( var face in faces ) cages.Add( new SBoxSubdivision.Cage( face, 1 ) );

		var made = SBoxSubdivision.Apply( points, cages ).Faces;

		var quads = 0;
		foreach ( var list in made ) quads += list.Count;

		Assert.AreEqual( faces.Count * 4, quads, "every face becomes four" );

		for ( var i = 0; i < before.Count; i++ )
		{
			Assert.AreEqual( before[i].x, points[i].x, 0.0001f, $"point {i} x" );
			Assert.AreEqual( before[i].y, points[i].y, 0.0001f, $"point {i} y" );
			Assert.AreEqual( before[i].z, points[i].z, 0.0001f, $"point {i} z" );
		}

		Assert.AreEqual( 4.0f, points[raised].z, 0.0001f, "the peak is still the peak" );
	}

	//Sculpted offsets apply to the surface: each one moves exactly one finished point
	[TestMethod]
	public void TheSculptedShapeIsPutOnTheSurface()
	{
		var (points, faces) = Grid( 1, 1 );

		var cages = new[] { new SBoxSubdivision.Cage( faces[0], 2 ) };

		//Four patches of three by three.
		var offsets = new Vector3[4 * 9];
		offsets[( 0 * 9 ) + 4] = new Vector3( 0, 0, -7 );

		var before = new List<Vector3>( points );
		var made = SBoxSubdivision.Apply( points, cages, offsets );

		Assert.IsTrue( made.Sculpted, made.Trouble ?? "the offsets should have been used" );
		Assert.IsNull( made.Trouble );

		//The cage itself never moves, sculpt or no sculpt.
		for ( var i = 0; i < before.Count; i++ )
			Assert.AreEqual( before[i].z, points[i].z, 0.0001f, $"cage point {i}" );

		var deepest = 0.0f;
		var moved = 0;

		foreach ( var quad in made.Faces[0] )
		{
			foreach ( var i in quad )
			{
				if ( points[i].z >= -0.0001f ) continue;

				moved++;
				if ( points[i].z < deepest ) deepest = points[i].z;
			}
		}

		Assert.AreEqual( -7.0f, deepest, 0.0001f, "the hollow is as deep as it was sculpted" );
		Assert.AreEqual( 1, CountBelow( points, made ), "and it is one point, not a region" );
	}

	private static int CountBelow( List<Vector3> points, SBoxSubdivision.Result made )
	{
		var seen = new HashSet<int>();

		foreach ( var list in made.Faces )
		{
			foreach ( var quad in list )
			{
				foreach ( var i in quad ) if ( points[i].z < -0.0001f ) seen.Add( i );
			}
		}

		return seen.Count;
	}

	//A displacement stream whose length doesn't add up is ignored entirely
	[TestMethod]
	public void AStreamThatDoesNotAddUpIsRefused()
	{
		var (points, faces) = Grid( 1, 1 );

		var cages = new[] { new SBoxSubdivision.Cage( faces[0], 2 ) };

		//Thirty six is right, so thirty five is not.
		var made = SBoxSubdivision.Apply( points, cages, new Vector3[35] );

		Assert.IsFalse( made.Sculpted );
		Assert.IsNotNull( made.Trouble, "and it says so" );

		foreach ( var quad in made.Faces[0] )
		{
			foreach ( var i in quad )
				Assert.AreEqual( 0.0f, points[i].z, 0.0001f, "the surface is the flat cage, cut" );
		}
	}


	//Two faces either side of an edge cut it in the same place. Otherwise every shared edge of a subdivided
	//patch is two edges with a crack between them, which on a terrace is a hairline of sky along every seam.
	[TestMethod]
	public void NeighboursShareThePointsOnTheEdgeBetweenThem()
	{
		var (points, faces) = Grid( 2, 1 );

		var cages = new List<SBoxSubdivision.Cage>();
		foreach ( var face in faces ) cages.Add( new SBoxSubdivision.Cage( face, 1 ) );

		var made = SBoxSubdivision.Apply( points, cages ).Faces;

		var left = new HashSet<int>();
		foreach ( var quad in made[0] ) foreach ( var i in quad ) left.Add( i );

		var right = new HashSet<int>();
		foreach ( var quad in made[1] ) foreach ( var i in quad ) right.Add( i );

		left.IntersectWith( right );

		//The two corners they already shared, and the point cut between them.
		Assert.AreEqual( 3, left.Count, "the shared edge is one edge, cut once" );
	}
}
