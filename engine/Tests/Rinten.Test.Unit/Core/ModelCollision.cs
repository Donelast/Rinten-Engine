namespace UnitTests;

//A hull with a corner count keeps its shape and loses its middle. The whole point of asking for one: a collision hull off a render mesh
//is twenty thousand points describing a shape ten of them would describe, and every one of them costs something to collide against.
[TestClass]
public class ModelCollisionTest
{
	[TestMethod]
	public void ReducingKeepsTheCorners()
	{
		var points = new System.Collections.Generic.List<Vector3>();

		//A cube, and a cloud of points inside it that no hull would ever need.
		var corners = new[]
		{
			new Vector3( -50, -50, -50 ), new Vector3( 50, -50, -50 ),
			new Vector3( -50, 50, -50 ), new Vector3( 50, 50, -50 ),
			new Vector3( -50, -50, 50 ), new Vector3( 50, -50, 50 ),
			new Vector3( -50, 50, 50 ), new Vector3( 50, 50, 50 ),
		};

		points.AddRange( corners );

		for ( var i = 0; i < 500; i++ )
		{
			var t = i / 500.0f;
			points.Add( new Vector3( -20 + t * 40, 20 - t * 40, -10 + t * 20 ) );
		}

		var reduced = Rinten.ModelCollision.Reduce( points.ToArray(), 32 );

		Assert.IsTrue( reduced.Length <= 32, $"asked for 32 points and got {reduced.Length}" );

		foreach ( var corner in corners )
		{
			Assert.IsTrue( reduced.Any( x => x.AlmostEqual( corner ) ),
				$"the hull lost the corner at {corner}" );
		}

		foreach ( var point in reduced )
		{
			Assert.IsTrue( point.x is -50 or 50 || point.y is -50 or 50 || point.z is -50 or 50,
				$"{point} is inside the cube, so no hull needs it" );
		}
	}

	[TestMethod]
	public void ReducingLeavesSmallShapesAlone()
	{
		var points = new[]
		{
			new Vector3( 0, 0, 0 ), new Vector3( 1, 0, 0 ),
			new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 ),
		};

		Assert.AreSame( points, Rinten.ModelCollision.Reduce( points, 16 ),
			"a shape with fewer points than the cap is already as small as it gets" );

		Assert.AreSame( points, Rinten.ModelCollision.Reduce( points, 0 ),
			"no cap means keep the lot" );
	}
}
