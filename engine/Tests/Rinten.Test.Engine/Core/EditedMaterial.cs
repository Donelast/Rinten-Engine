using System;

namespace EngineTests.Core;

//A material value set from code is picked up by the renderer on the next draw
[TestClass]
[DoNotParallelize]
public class EditedMaterialTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void SettingANumberChangesWhatTheRendererDrawsWith()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		if ( !CoreRenderDevice.Initialize( validation: false ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		try
		{
			//Under a path, so the renderer can be asked for it the way a mesh asks - by name rather than by object.
			var path = $"materials/test/{Guid.NewGuid():N}.mat";

			var material = Material.Create( path, "shaders/standard.shader", anonymous: false );

			Assert.IsNotNull( material );

			var before = CoreResources.Material( path );

			//Nothing has been set, so this is whatever the path resolved to - there is no file behind it, which
			//is the fallback, and that is fine: what matters is that it does not go through the live path.
			Assert.IsNull( Material.Edited( path ), "nothing has been set on it yet" );

			material.Set( "g_flRoughness", 0.125f );

			Assert.IsNotNull( Material.Edited( path ), "it has been set on now" );

			var after = CoreResources.Material( path );

			Assert.AreNotEqual( 0ul, after, "an edited material should still resolve" );
			Assert.AreNotEqual( before, after,
				"an edited material should be drawn from what it now says, not from the path cache" );

			//And again with the same value: the same material, not a new one every frame.
			var again = CoreResources.Material( path );

			Assert.AreEqual( after, again, "an unchanged material should not be rebuilt" );

			//A change makes a new one.
			material.Set( "g_flRoughness", 0.875f );

			Assert.AreNotEqual( again, CoreResources.Material( path ),
				"setting a different value should reach the device" );
		}
		finally
		{
			CoreRenderDevice.Shutdown();
		}
	}
}
