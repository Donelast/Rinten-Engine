using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//ApplyTransform moves mesh, bones and bodies; covers body rotation and non-uniform scale normals
[TestClass]
[DoNotParallelize]
public unsafe class ModelApplyTransformTests
{
	[TestMethod]
	public void BodyRotationFollowsTheWholeModelTransform()
	{
		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			//Boneless, because that is the body whose orientation the whole-model turn is actually its own to take - one on a bone gets its turn from the bone
			//instead, which is what ABoneRelativeShapeDoesNotTakeTheOffsetASecondTime covers. Written at identity either way - see ModelCollision.BodyFor.
			var identity = stackalloc float[7] { 0, 0, 0, 0, 0, 0, 1 };
			var body = builder.AddBody( "body", "", identity, 1f, 0f, 0f, 1f );
			Assert.IsTrue( body >= 0 );

			var center = stackalloc float[3];
			builder.AddSphere( body, "", center, 4f );

			var turn = Rotation.FromAxis( Vector3.Up, 90f );

			var pos = stackalloc float[3] { 0, 0, 0 };
			var rot = stackalloc float[4] { turn.x, turn.y, turn.z, turn.w };
			var scale = stackalloc float[3] { 1, 1, 1 };
			builder.ApplyTransform( pos, rot, scale );

			var model = builder.Build();
			Assert.IsNotNull( model );

			try
			{
				Assert.AreEqual( 1, model.BodyCount );

				var t = stackalloc float[7];
				model.BodyTransform( 0, t );

				var bodyRotation = new Rotation( t[3], t[4], t[5], t[6] );

				//Was identity going in - a body that never turns with the model would still read back as identity here.
				Assert.AreNotEqual( 0f, bodyRotation.Angle(), 0.01f,
					"the body's own rotation should have turned with the model" );
				Assert.AreEqual( turn.Angle(), bodyRotation.Angle(), 0.01f );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			builder.Dispose();
		}
	}

	//A hull hung off a bone is measured from that bone, and the bone has already been moved by the transform through its inverse
	//bind - so laying the offset on the shape as well moves collision out by it a second time, away from the mesh it belongs to.
	[TestMethod]
	public void ABoneRelativeShapeDoesNotTakeTheOffsetASecondTime()
	{
		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			var identity = stackalloc float[7] { 0, 0, 0, 0, 0, 0, 1 };
			var body = builder.AddBody( "limb", "bone", identity, 1f, 0f, 0f, 1f );
			Assert.IsTrue( body >= 0 );

			var center = stackalloc float[3] { 0, 0, 5 };
			builder.AddSphere( body, "", center, 4f );

			var pos = stackalloc float[3] { 0, 0, 100 };
			var rot = stackalloc float[4] { 0, 0, 0, 1 };
			var scale = stackalloc float[3] { 2, 2, 2 };
			builder.ApplyTransform( pos, rot, scale );

			var model = builder.Build();
			Assert.IsNotNull( model );

			try
			{
				var primitive = stackalloc float[7];
				model.ShapePrimitive( 0, 0, primitive );

				//Scaled, so 5 becomes 10 - a limb twice as long wants collision twice as long.
				//But NOT offset: 110 would be the bone's own hundred laid on the shape as well.
				Assert.AreEqual( 10f, primitive[2], 0.001f,
					"a bone-relative shape should take the scale" );
				Assert.AreEqual( 8f, primitive[3], 0.001f,
					"and its radius with it" );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			builder.Dispose();
		}
	}

	//A boneless body is the other case: nothing else has moved its shapes, so they take the whole transform.
	[TestMethod]
	public void ABonelessShapeTakesTheWholeTransform()
	{
		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			var identity = stackalloc float[7] { 0, 0, 0, 0, 0, 0, 1 };
			var body = builder.AddBody( "prop", "", identity, 1f, 0f, 0f, 1f );
			Assert.IsTrue( body >= 0 );

			var center = stackalloc float[3] { 0, 0, 5 };
			builder.AddSphere( body, "", center, 4f );

			var pos = stackalloc float[3] { 0, 0, 100 };
			var rot = stackalloc float[4] { 0, 0, 0, 1 };
			var scale = stackalloc float[3] { 2, 2, 2 };
			builder.ApplyTransform( pos, rot, scale );

			var model = builder.Build();
			Assert.IsNotNull( model );

			try
			{
				var primitive = stackalloc float[7];
				model.ShapePrimitive( 0, 0, primitive );

				Assert.AreEqual( 110f, primitive[2], 0.001f,
					"a model-relative shape should take the offset as well as the scale" );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			builder.Dispose();
		}
	}

	[TestMethod]
	public void NonUniformScaleTiltsNormalsAwayFromTheStretchedAxis()
	{
		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			//A 45-degree normal in the XY plane. A rotation-only transform (the bug) leaves a 45-degree normal at 45 degrees no matter
			//what the scale is - X and Y stretched differently is exactly the case where that stops being square to the surface.
			var normal = new Vector3( 1, 1, 0 );
			normal = normal / normal.Length;

			var vertex = new Vertex
			{
				Position = new Vector3( 1, 1, 0 ),
				Normal = normal,
				Tangent = new Vector4( 0, 0, 1, -1 ),
			};

			fixed ( Vertex* v = new[] { vertex } )
				builder.AddVertices( v, 1 );

			var pos = stackalloc float[3] { 0, 0, 0 };
			var rot = stackalloc float[4] { 0, 0, 0, 1 };

			//Stretched 2x along X only.
			var scale = stackalloc float[3] { 2, 1, 1 };
			builder.ApplyTransform( pos, rot, scale );

			var model = builder.Build();
			Assert.IsNotNull( model );

			try
			{
				var read = new Vertex[1];
				fixed ( Vertex* v = read )
					model.CopyVertices( v, 1 );

				var result = read[0].Normal;

				Assert.AreEqual( 1f, result.Length, 0.001f, "a transformed normal should stay unit length" );

				//Stretching the surface along X tilts its normal toward Y - the buggy rotation-only transform left the two components equal.
				Assert.IsTrue( result.y > result.x,
					$"expected the normal to tilt toward Y away from the stretched X axis, got {result}" );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			builder.Dispose();
		}
	}
}
