namespace ResourceTests;

[TestClass]
public class ResourceRegistrationTests
{
	//Unregistering a stale duplicate (e.g. via finalizer) must not remove the live resource
	[TestMethod]
	public void UnregisteringStaleDuplicateKeepsLiveResourceRegistered()
	{
		var path = "___stale_duplicate.prefab";

		var stale = new PrefabFile();
		stale.Register( path );

		var live = new PrefabFile();
		live.Register( path );

		try
		{
			Assert.AreSame( live, ResourceLibrary.Get<PrefabFile>( path ) );

			Game.Resources.Unregister( stale );

			Assert.AreSame( live, ResourceLibrary.Get<PrefabFile>( path ), "Unregistering a stale duplicate must not remove the live resource" );
		}
		finally
		{
			Game.Resources.Unregister( live );
		}

		Assert.IsNull( ResourceLibrary.Get<PrefabFile>( path ), "Unregistering the live resource must remove it" );
	}

	//Same identity rule for weak registrations - a stale weak entry replaced by another instance must not be removed by the stale instance's unregister.
	[TestMethod]
	public void UnregisteringStaleDuplicateKeepsLiveWeakResourceRegistered()
	{
		var path = "___stale_weak_duplicate.prefab";

		var stale = new PrefabFile();
		stale.RegisterWeakResourceId( path );

		var live = new PrefabFile();
		live.RegisterWeakResourceId( path );

		try
		{
			Assert.AreSame( live, ResourceLibrary.Get<PrefabFile>( path ) );

			Game.Resources.Unregister( stale );

			Assert.AreSame( live, ResourceLibrary.Get<PrefabFile>( path ), "Unregistering a stale duplicate must not remove the live weak resource" );
		}
		finally
		{
			Game.Resources.Unregister( live );
		}

		Assert.IsNull( ResourceLibrary.Get<PrefabFile>( path ), "Unregistering the live resource must remove it" );
	}
}
