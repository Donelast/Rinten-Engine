namespace SceneTests;

//A scene object's transform is the renderer's only source of position; a no-op setter hid gizmos
[TestClass]
[DoNotParallelize]
public class SceneObjectTransformTest
{
	[TestMethod]
	public void AnUnplacedObjectIsNotCollapsed()
	{
		var world = new SceneWorld();

		try
		{
			var found = new SceneDynamicObject( world );

			Assert.AreEqual( Vector3.One, found.Transform.Scale, "A scale of zero draws nothing." );
			Assert.AreEqual( Rotation.Identity, found.Transform.Rotation );
			Assert.AreEqual( Vector3.Zero, found.Transform.Position );
		}
		finally
		{
			world.Delete();
		}
	}

	[TestMethod]
	public void TheTransformIsWhatWasSet()
	{
		var world = new SceneWorld();

		try
		{
			var found = new SceneDynamicObject( world );

			var wanted = new Transform( new Vector3( 10, 20, 30 ), Rotation.From( 0, 90, 0 ), 2.0f );

			found.Transform = wanted;

			Assert.AreEqual( wanted.Position, found.Transform.Position );
			Assert.AreEqual( wanted.Scale, found.Transform.Scale );
			Assert.AreEqual( wanted.Position, found.Position );
		}
		finally
		{
			world.Delete();
		}
	}

	//Position and rotation both write through the transform, so they went the same way it did.
	[TestMethod]
	public void PositionAndRotationWriteThrough()
	{
		var world = new SceneWorld();

		try
		{
			var found = new SceneDynamicObject( world );

			found.Position = new Vector3( 1, 2, 3 );
			found.Rotation = Rotation.From( 0, 45, 0 );

			Assert.AreEqual( new Vector3( 1, 2, 3 ), found.Position );
			Assert.AreEqual( new Vector3( 1, 2, 3 ), found.Transform.Position );

			//Whatever else moved, the scale must not have been thrown away with it.
			Assert.AreEqual( Vector3.One, found.Transform.Scale );
		}
		finally
		{
			world.Delete();
		}
	}

	//The version is how a cache tells that something moved without keeping a copy of where it was.
	[TestMethod]
	public void MovingBumpsTheVersionAndStayingStillDoesNot()
	{
		var world = new SceneWorld();

		try
		{
			var found = new SceneDynamicObject( world );

			var before = found.TransformVersion;

			found.Position = new Vector3( 5, 0, 0 );
			var moved = found.TransformVersion;

			Assert.AreNotEqual( before, moved, "Moving should be noticed." );

			found.Position = new Vector3( 5, 0, 0 );

			Assert.AreEqual( moved, found.TransformVersion, "Setting the same place again is not a move." );
		}
		finally
		{
			world.Delete();
		}
	}
}
