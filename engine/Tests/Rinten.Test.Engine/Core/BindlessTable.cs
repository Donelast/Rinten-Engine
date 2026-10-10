namespace EngineTests.Core;

//The bindless texture table is sized from device limits; overflowing it draws wrong textures
[TestClass]
[DoNotParallelize]
public class BindlessTableTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void ItHoldsAsManyAsTheDeviceAllows()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core" ); return; }
		if ( !CoreRenderDevice.Initialize( validation: false ) ) { Assert.Inconclusive( "no device" ); return; }

		try
		{
			var slots = CoreRender.BindlessSlots();

			if ( slots == 0 )
			{
				Assert.Inconclusive( "this device has no descriptor indexing" );
				return;
			}

			//The floor is what the shaders clamp an out-of-range index to, so a table below it would be worse than one merely too small.
			Assert.IsTrue( slots >= 4096, $"the table holds {slots} pictures" );

			//And on anything this engine is meant to run on, far more than that: every desktop driver allows hundreds of thousands of these.
			Assert.IsTrue( slots >= 16384,
				$"the table holds {slots} pictures, and the device was asked for 16384" );
		}
		finally
		{
			CoreRenderDevice.Shutdown();
		}
	}
}
