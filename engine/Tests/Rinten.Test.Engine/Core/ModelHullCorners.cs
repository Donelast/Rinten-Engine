using System;

namespace EngineTests;

//Hull input is capped so Box3D's 255 half-edge limit holds; over it, the hull silently vanishes
[TestClass]
public class ModelHullCornersTest
{
	//A dense ball of points, which is what a weapon's low-poly collision mesh amounts to.
	private static Vector3[] Cloud( int count )
	{
		var points = new Vector3[count];

		const float golden = 2.39996323f;

		for ( var i = 0; i < count; i++ )
		{
			var z = 1.0f - (2.0f * i + 1.0f) / count;
			var radius = MathF.Sqrt( MathF.Max( 0.0f, 1.0f - z * z ) );
			var angle = golden * i;

			//Off the sphere by a little, so the cloud is not a shape whose hull is trivially every point of it.
			var scale = 0.6f + 0.4f * ((i * 37) % 11) / 10.0f;

			points[i] = new Vector3( radius * MathF.Cos( angle ), radius * MathF.Sin( angle ), z ) * scale;
		}

		return points;
	}

	[TestMethod]
	public unsafe void AHullIsCutDownToWhatTheBuilderCanActuallyMake()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var world = CorePhysics.WorldCreate();

		Assert.AreNotEqual( 0ul, world );

		try
		{
			var cloud = Cloud( 4000 );

			//The cloud itself is not handed over. Box3D answers a hull it cannot build with nothing, and on a cloud this size it
			//does worse than that - it walks off the end of a 255-entry array on its own stack and takes the process with it.
			var reduced = ModelCollision.Reduce( cloud, 44 );

			Assert.IsTrue( reduced.Length <= 44, $"reduced to {reduced.Length}" );

			Assert.AreNotEqual( 0ul, Hull( world, reduced ),
				"forty-four corners is inside every one of Box3D's limits" );
		}
		finally
		{
			CorePhysics.WorldDestroy( world );
		}
	}

	private static ulong Hull( ulong world, Vector3[] points )
	{
		var body = CorePhysics.BodyCreate( world, CorePhysics.MotionType.Static,
			Vector3.Zero, Rotation.Identity );

		if ( body == 0 ) return 0;

		var flat = new float[points.Length * 3];

		for ( var i = 0; i < points.Length; i++ )
		{
			flat[i * 3] = points[i].x;
			flat[i * 3 + 1] = points[i].y;
			flat[i * 3 + 2] = points[i].z;
		}

		return CorePhysics.BodyAddHull( body, flat, 1.0f, false );
	}
}
