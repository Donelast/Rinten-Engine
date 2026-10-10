namespace SceneTests;

//A scene world holds objects until they're deleted; leaked per-frame gizmos once cost ~9GB
[TestClass]
[DoNotParallelize]
public class SceneObjectLifetimeTest
{
	[TestMethod]
	public void DeleteTakesTheObjectOutOfItsWorld()
	{
		var world = new SceneWorld();

		try
		{
			var found = new SceneDynamicObject( world );

			Assert.AreEqual( 1, world.InternalSceneObjects.Count );
			Assert.IsTrue( found.IsValid() );

			found.Delete();

			Assert.AreEqual( 0, world.InternalSceneObjects.Count );
			Assert.IsFalse( found.IsValid(), "A deleted object is not valid." );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void DeletingTwiceIsDeletingOnce()
	{
		var world = new SceneWorld();

		try
		{
			var found = new SceneDynamicObject( world );

			found.Delete();
			found.Delete();

			Assert.AreEqual( 0, world.InternalSceneObjects.Count );
		}
		finally
		{
			world.Delete();
		}
	}

	//The shape of the leak that was: a frame's worth of objects made and thrown away,
	//over and over. What is left in the world afterwards is what would have accumulated.
	[TestMethod]
	public void ObjectsDrawnAndDroppedEveryFrameDoNotAccumulate()
	{
		var world = new SceneWorld();

		try
		{
			for ( var frame = 0; frame < 64; ++frame )
			{
				var drawn = new SceneDynamicObject[8];

				for ( var at = 0; at < drawn.Length; ++at )
					drawn[at] = new SceneDynamicObject( world );

				Assert.AreEqual( drawn.Length, world.InternalSceneObjects.Count );

				foreach ( var found in drawn ) found.Delete();

				Assert.AreEqual( 0, world.InternalSceneObjects.Count, $"Frame {frame} left objects behind." );
			}
		}
		finally
		{
			world.Delete();
		}
	}

	//A world that goes takes its contents with it, and does not leave them thinking they are still somewhere.
	[TestMethod]
	public void DeletingTheWorldDeletesWhatIsInIt()
	{
		var world = new SceneWorld();
		var found = new SceneDynamicObject( world );

		world.Delete();

		Assert.AreEqual( 0, world.InternalSceneObjects.Count );
		Assert.IsFalse( found.IsValid() );
	}
}
