using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;

namespace EngineTests.Core;

//A pose uploaded once per frame is shared by every pass; its offset is valid only within that frame
[TestClass]
[DoNotParallelize]
public unsafe class PoseReuseTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void OnePoseOneUploadAndOnlyForItsOwnFrame()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }
		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var view = 0ul;

		try
		{
			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view, $"a view should be made: {CoreRenderDevice.Description}" );

			var pose = new System.Numerics.Matrix4x4[3];
			var other = new System.Numerics.Matrix4x4[3];

			for ( var i = 0; i < 3; i++ )
			{
				pose[i] = System.Numerics.Matrix4x4.CreateTranslation( i, 0, 0 );
				other[i] = System.Numerics.Matrix4x4.CreateTranslation( 0, i, 0 );
			}

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ), "the frame should begin" );

			var first = CoreRenderDevice.Bones( pose );
			var again = CoreRenderDevice.Bones( pose );
			var second = CoreRenderDevice.Bones( other );

			CoreRenderDevice.EndViewFrame( view );

			Assert.AreNotEqual( 0u, first, $"the pose should have gone over: {CoreRenderDevice.Description}" );
			Assert.AreEqual( first, again, "the same pose asked for twice should be the same upload" );
			Assert.AreNotEqual( first, second, "two poses should not land on top of each other" );

			//And the next frame, which has written nothing yet, does not know that number: what it named
			//belongs to a frame that has been submitted and whose scratch is being reused as this runs.
			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ), "the second frame should begin" );

			var stale = CoreRenderDevice.DrawSkinnedAt( 0, 0, 0, 0, 3, first );

			var fresh = CoreRenderDevice.Bones( pose );

			CoreRenderDevice.EndViewFrame( view );

			Assert.IsFalse( stale, "an offset from the frame before should be refused" );
			Assert.AreNotEqual( 0u, fresh, "the new frame should take the same pose again" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
