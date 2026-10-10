using System;

namespace EngineTests.Core;

//A view can be polled for frame completion without waiting; only the idle and waited ends asserted
[TestClass]
[DoNotParallelize]
public class ViewFrameReadyTests
{
	[TestMethod]
	public void AViewNobodyIsDrawingIsFinished()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var view = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 256, 256 );

			Assert.AreNotEqual( 0ul, view );

			//Never submitted: there is nothing to be waiting for.
			Assert.IsTrue( CoreRenderDevice.ViewFrameReady( view ),
				"a view that has never drawn should not be busy" );

			//And a frame that was waited for by the ending itself.
			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );
			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			Assert.IsTrue( CoreRenderDevice.ViewFrameReady( view ),
				"a frame that has been waited for is finished by definition" );

			//The one that matters, reported rather than asserted: after a submission nobody waited for, the card is usually still at it.
			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.White ) );
			Assert.IsTrue( CoreRenderDevice.EndViewFrameAsync( view ) );

			Console.WriteLine( CoreRenderDevice.ViewFrameReady( view )
				? "the card had already finished the frame nobody waited for"
				: "the card was still drawing the frame nobody waited for - which is the case this is for" );

			//However that came out, it must become finished, and without anybody having waited on the processor to make it so.
			var ready = false;

			for ( var i = 0; i < 10000 && !ready; i++ )
			{
				ready = CoreRenderDevice.ViewFrameReady( view );
			}

			Assert.IsTrue( ready, "a submitted frame should finish" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
