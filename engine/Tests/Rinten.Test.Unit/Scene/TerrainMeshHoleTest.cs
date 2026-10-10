namespace Rinten.Test.Unit;

[TestClass]
public class TerrainMeshHoleTest
{
	//Filling a hole closes cuts in the terrain mesh but leaves the terrain's outer outline alone
	[TestMethod]
	public void FillHoleClosesAnOpeningAndLeavesTheOutline()
	{
		const int side = 6;
		var mesh = TerrainMesh.FromHeights( new ushort[side * side], null, side, side, 1.0f );
		var before = mesh.TriangleCount;

		Assert.IsNull( mesh.Validate() );

		mesh.FillHole( Vector3.Zero, 1.5f );

		Assert.AreEqual( before, mesh.TriangleCount, "The map's own outline is not a hole" );
		Assert.IsNull( mesh.Validate() );

		var cut = TriangleNear( mesh, new Vector3( 3.1667f, 0.0f, 2.8333f ) );
		var centre = Centroid( mesh, cut );

		mesh.DeleteTriangles( centre, 0.15f );

		Assert.AreEqual( before - 1, mesh.TriangleCount, "One interior triangle is the whole cut" );
		Assert.IsFalse( mesh.Trace( centre + Vector3.Up * 5.0f, Vector3.Down, 10.0f, out _, out _, out _ ), "The cut is a gap a ray falls through" );
		Assert.IsTrue( mesh.TraceOpening( centre + Vector3.Up * 5.0f, Vector3.Down, 10.0f, 1.5f, out var across, out _ ), "Ctrl can still aim into the gap" );
		Assert.AreEqual( 0.0f, across.y, 0.05f );

		mesh.FillHole( centre, 1.5f );

		Assert.AreEqual( before, mesh.TriangleCount, "The one triangle comes back" );
		Assert.IsNull( mesh.Validate() );
		Assert.IsTrue( mesh.Trace( centre + Vector3.Up * 5.0f, Vector3.Down, 10.0f, out _, out _, out _ ) );

		var wide = new Vector3( 3.0f, 0.0f, 3.0f );
		mesh.DeleteTriangles( wide, 1.2f );
		var opened = mesh.TriangleCount;

		Assert.IsTrue( opened < before );

		mesh.FillHole( wide, 4.0f );

		Assert.IsTrue( mesh.TriangleCount > opened, "The wider hole gains triangles back" );
		Assert.IsNull( mesh.Validate() );
		Assert.IsTrue( mesh.Trace( wide + Vector3.Up * 5.0f, Vector3.Down, 10.0f, out var travelled, out _, out _ ), "The closed hole is ground again" );
		Assert.AreEqual( 5.0f, travelled, 0.05f );
	}

	static Vector3 Centroid( TerrainMesh mesh, int triangle )
	{
		mesh.GetTriangle( triangle, out var a, out var b, out var c );
		return ( mesh.PositionOf( a ) + mesh.PositionOf( b ) + mesh.PositionOf( c ) ) / 3.0f;
	}

	static int TriangleNear( TerrainMesh mesh, Vector3 at )
	{
		var best = -1;
		var bestDistance = float.MaxValue;

		for ( var t = 0; t < mesh.TriangleSlots; t++ )
		{
			if ( !mesh.IsTriangleAlive( t ) ) continue;

			var distance = ( Centroid( mesh, t ) - at ).LengthSquared;
			if ( distance >= bestDistance ) continue;

			bestDistance = distance;
			best = t;
		}

		return best;
	}
}
