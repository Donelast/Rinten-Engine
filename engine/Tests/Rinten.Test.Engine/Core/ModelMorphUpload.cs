using System;
using Rinten;

namespace EngineTests;

//A model's blend shape buffers are created and attached to the mesh the renderer draws
[TestClass]
[DoNotParallelize]
public class ModelMorphUploadTests
{
	private static Mesh Triangle()
	{
		var mesh = new Mesh();

		mesh.CreateVertexBuffer( 3, new[]
		{
			new Vertex { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new Vertex { Position = new Vector3( 1, 0, 0 ), Normal = Vector3.Up },
			new Vertex { Position = new Vector3( 1, 1, 0 ), Normal = Vector3.Up },
		}.AsSpan() );

		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		return mesh;
	}

	[TestMethod]
	public void AModelsShapesReachTheDevice()
	{
		ThreadSafe.MarkMainThread();

		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		try
		{
			var mesh = Triangle();

			mesh.AddMorph( "smile", new[]
			{
				new MorphDelta( 0, new Vector3( 0, 0, 1 ), Vector3.Zero ),
				new MorphDelta( 2, new Vector3( 0, 0, 2 ), Vector3.Zero ),
			} );

			var model = new ModelBuilder().AddMesh( mesh ).Create();

			Assert.IsTrue( model.IsValid(), "the model should have been built" );
			Assert.AreEqual( 1, model.MorphCount, "and should carry the shape" );

			var loaded = CoreResources.Mesh( model );

			Assert.IsNotNull( loaded, $"the model should go onto the device: {CoreRenderDevice.Description}" );

			Assert.AreEqual( 1, loaded.MorphCount, "the mesh should know how many shapes it has" );
			Assert.AreNotEqual( 0ul, loaded.MorphDeltas, "the deltas should be on the device" );
			Assert.AreNotEqual( 0ul, loaded.MorphRuns, "and the run each vertex owns with them" );
		}
		finally
		{
			CoreResources.Clear();
			CoreRenderDevice.Shutdown();
		}
	}
}
