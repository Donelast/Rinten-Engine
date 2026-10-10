using Rinten.Engine.Utility.RayTrace;

namespace SceneTests;

//Ray traces against drawn meshes, used for editor picking without colliders
[TestClass]
[DoNotParallelize]
public class MeshTraceTest
{
	//A box in the world, as a scene object with its bounds pinned. Bounds set by hand are world space
	//and stay where they are put - which is what something with no readable triangles is traced against.
	private static SceneObject Box( SceneWorld world, Vector3 position, float size = 32.0f )
	{
		return new SceneDynamicObject( world )
		{
			Bounds = BBox.FromPositionAndSize( position, size ),
		};
	}

	private static MeshTraceRequest Request( SceneWorld world ) => MeshTraceRequest.From( default, world, 0 );

	private static MeshTraceRequest.Result Trace( SceneWorld world, Vector3 from, Vector3 to )
		=> Request( world ).Ray( from, to ).Run();

	//-- the walk ---------------------------------------------------------------

	[TestMethod]
	public void AMissIsAMissAndNotAnException()
	{
		var world = new SceneWorld();

		try
		{
			Box( world, Vector3.Zero );

			var from = new Vector3( -1000, 500, 0 );
			var to = new Vector3( 1000, 500, 0 );

			Assert.IsFalse( Trace( world, from, to ).Hit );

			var all = Request( world ).Ray( from, to ).RunAll();

			Assert.IsNotNull( all, "A trace that hits nothing returns nothing, never null." );
			Assert.AreEqual( 0, all.Length );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void ARayThroughABoxHitsItsNearFace()
	{
		var world = new SceneWorld();

		try
		{
			Box( world, Vector3.Zero, 32.0f );

			var result = Trace( world, new Vector3( -100, 0, 0 ), new Vector3( 100, 0, 0 ) );

			Assert.IsTrue( result.Hit );

			//The box is 32 across, so its near face is at -16.
			Assert.AreEqual( -16.0f, result.HitPosition.x, 0.01f );
			Assert.AreEqual( 84.0f, result.Distance, 0.01f );
			Assert.AreEqual( -1.0f, result.Normal.x, 0.01f, "Facing back the way the ray came." );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void SomethingPastTheEndOfTheSegmentIsNotHit()
	{
		var world = new SceneWorld();

		try
		{
			Box( world, new Vector3( 200, 0, 0 ) );

			Assert.IsTrue( Trace( world, new Vector3( -100, 0, 0 ), new Vector3( 1000, 0, 0 ) ).Hit );
			Assert.IsFalse( Trace( world, new Vector3( -100, 0, 0 ), new Vector3( 100, 0, 0 ) ).Hit );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void TheNearestOfSeveralComesFirst()
	{
		var world = new SceneWorld();

		try
		{
			Box( world, new Vector3( 400, 0, 0 ) );
			Box( world, new Vector3( 100, 0, 0 ) );
			Box( world, new Vector3( 700, 0, 0 ) );

			var from = new Vector3( -100, 0, 0 );
			var to = new Vector3( 1000, 0, 0 );

			var nearest = Trace( world, from, to );

			Assert.IsTrue( nearest.Hit );
			Assert.AreEqual( 84.0f, nearest.HitPosition.x, 0.01f );

			var all = Request( world ).Ray( from, to ).RunAll();

			Assert.AreEqual( 3, all.Length );

			for ( var at = 1; at < all.Length; ++at )
				Assert.IsTrue( all[at - 1].Fraction <= all[at].Fraction, "Nearest first." );
		}
		finally
		{
			world.Delete();
		}
	}

	//What is not drawn is not under the cursor.
	[TestMethod]
	public void SomethingNotBeingDrawnIsNotHit()
	{
		var world = new SceneWorld();

		try
		{
			var found = Box( world, Vector3.Zero );
			found.RenderingEnabled = false;

			Assert.IsFalse( Trace( world, new Vector3( -100, 0, 0 ), new Vector3( 100, 0, 0 ) ).Hit );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void AnExcludedTagIsNotHit()
	{
		var world = new SceneWorld();

		try
		{
			var found = Box( world, Vector3.Zero );
			found.Tags.Add( "hidden" );

			var result = Request( world )
				.WithoutTag( "hidden" )
				.Ray( new Vector3( -100, 0, 0 ), new Vector3( 100, 0, 0 ) )
				.Run();

			Assert.IsFalse( result.Hit );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void ARequiredTagIsNeeded()
	{
		var world = new SceneWorld();

		try
		{
			var tagged = Box( world, new Vector3( 300, 0, 0 ) );
			tagged.Tags.Add( "pickable" );

			Box( world, new Vector3( 100, 0, 0 ) );

			var result = Request( world )
				.WithTag( "pickable" )
				.Ray( new Vector3( -100, 0, 0 ), new Vector3( 1000, 0, 0 ) )
				.Run();

			Assert.IsTrue( result.Hit );
			Assert.AreEqual( 284.0f, result.HitPosition.x, 0.01f, "The nearer box has no tag." );
		}
		finally
		{
			world.Delete();
		}
	}

	//The object it hit comes back with the result - it is what the editor turns into a selection.
	[TestMethod]
	public void TheHitObjectComesBack()
	{
		var world = new SceneWorld();

		try
		{
			var found = Box( world, Vector3.Zero );

			var result = Trace( world, new Vector3( -100, 0, 0 ), new Vector3( 100, 0, 0 ) );

			Assert.IsTrue( result.Hit );
			Assert.AreSame( found, result.SceneObject );
		}
		finally
		{
			world.Delete();
		}
	}

	//-- the triangle ------------------------------------------------------------ What a mapping mesh is traced against, one triangle
	//at a time. Tested straight rather than through a mesh: this is the arithmetic, and it is where a picking bug would actually live.

	private static readonly Vector3 A = new( 0, -10, -10 );
	private static readonly Vector3 B = new( 0, 10, -10 );
	private static readonly Vector3 C = new( 0, 0, 10 );

	[TestMethod]
	public void ARayThroughATriangleHitsIt()
	{
		var hit = MeshTraceRequest.TraceTriangle( A, B, C,
			new Vector3( -100, 0, 0 ), new Vector3( 200, 0, 0 ), 0, out var fraction, out var normal );

		Assert.IsTrue( hit );

		//Half way along a segment that starts a hundred out and runs two hundred.
		Assert.AreEqual( 0.5f, fraction, 0.001f );
		Assert.AreEqual( 1.0f, System.MathF.Abs( normal.x ), 0.001f );
	}

	[TestMethod]
	public void ARayBesideATriangleMissesIt()
	{
		Assert.IsFalse( MeshTraceRequest.TraceTriangle( A, B, C,
			new Vector3( -100, 50, 0 ), new Vector3( 200, 0, 0 ), 0, out _, out _ ) );
	}

	//The fraction is along the segment, so it does not care how long the segment is - which
	//is what lets the same routine work in an object's own space whatever it is scaled by.
	[TestMethod]
	public void TheFractionIsAlongTheSegment()
	{
		MeshTraceRequest.TraceTriangle( A, B, C,
			new Vector3( -100, 0, 0 ), new Vector3( 200, 0, 0 ), 0, out var near, out _ );

		MeshTraceRequest.TraceTriangle( A, B, C,
			new Vector3( -100, 0, 0 ), new Vector3( 400, 0, 0 ), 0, out var far, out _ );

		Assert.AreEqual( 0.5f, near, 0.001f );
		Assert.AreEqual( 0.25f, far, 0.001f );
	}

	[TestMethod]
	public void NothingBehindTheStartIsHit()
	{
		//Pointing away from the triangle entirely.
		Assert.IsFalse( MeshTraceRequest.TraceTriangle( A, B, C,
			new Vector3( -100, 0, 0 ), new Vector3( -200, 0, 0 ), 0, out _, out _ ) );
	}

	[TestMethod]
	public void CullingKeepsTheSideItWasAskedFor()
	{
		var from = new Vector3( -100, 0, 0 );
		var delta = new Vector3( 200, 0, 0 );

		//Which side is which depends on the winding; what matters is that the two modes disagree and taking both agrees with each of them.
		var front = MeshTraceRequest.TraceTriangle( A, B, C, from, delta, 2, out _, out _ );
		var back = MeshTraceRequest.TraceTriangle( A, B, C, from, delta, 1, out _, out _ );
		var either = MeshTraceRequest.TraceTriangle( A, B, C, from, delta, 0, out _, out _ );

		Assert.IsTrue( either );
		Assert.AreNotEqual( front, back, "One side or the other, not both." );
		Assert.IsTrue( front || back );
	}
}
