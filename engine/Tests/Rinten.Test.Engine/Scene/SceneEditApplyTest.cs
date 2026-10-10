namespace SceneTests;

using System.Linq;
using System.Text.Json.Nodes;
using Editor;

//Restoring a snapshot in place keeps surviving objects identical, so undo needs no panel rebuild
[TestClass]
[DoNotParallelize]
public class SceneEditApplyTest : SceneTest
{
	private static Scene WithObjects( params string[] names )
	{
		var scene = new Scene();

		using var scope = scene.Push();

		foreach ( var name in names )
		{
			var go = new GameObject( true, name );
			go.Parent = scene;
		}

		return scene;
	}

	[TestMethod]
	public void WhatWasDeletedComesBack()
	{
		var scene = WithObjects( "One", "Two", "Three" );

		using var scope = scene.Push();

		var snapshot = scene.Serialize();

		scene.Children.First( x => x.Name == "Two" ).Destroy();
		scene.ProcessDeletes();

		Assert.AreEqual( 2, scene.Children.Count );

		SceneEdit.ApplyScene( scene, snapshot );

		Assert.AreEqual( 3, scene.Children.Count );
		Assert.IsTrue( scene.Children.Any( x => x.Name == "Two" ) );
	}

	[TestMethod]
	public void WhatSurvivedIsStillTheSameObject()
	{
		var scene = WithObjects( "One", "Two" );

		using var scope = scene.Push();

		var kept = scene.Children.First( x => x.Name == "One" );
		var snapshot = scene.Serialize();

		scene.Children.First( x => x.Name == "Two" ).Destroy();
		scene.ProcessDeletes();

		SceneEdit.ApplyScene( scene, snapshot );

		var after = scene.Children.First( x => x.Name == "One" );

		Assert.AreSame( kept, after, "an object that did not change is not built again" );
		Assert.IsTrue( kept.IsValid(), "and it is still the one everything else is holding" );
	}

	[TestMethod]
	public void WhatWasAddedAfterwardsGoes()
	{
		var scene = WithObjects( "One" );

		using var scope = scene.Push();

		var snapshot = scene.Serialize();

		var added = new GameObject( true, "Two" );
		added.Parent = scene;

		Assert.AreEqual( 2, scene.Children.Count );

		SceneEdit.ApplyScene( scene, snapshot );
		scene.ProcessDeletes();

		Assert.AreEqual( 1, scene.Children.Count );
		Assert.AreEqual( "One", scene.Children[0].Name );
	}

	//A snapshot this cannot read is left alone rather than acted on. Reading the ids wrongly
	//would mean nothing in it is recognised, and the next thing it would do is empty the scene.
	[TestMethod]
	public void ASnapshotItCannotReadChangesNothing()
	{
		var scene = WithObjects( "One", "Two" );

		using var scope = scene.Push();

		var nonsense = new JsonObject
		{
			["GameObjects"] = new JsonArray( new JsonObject { ["Name"] = "Ghost" } ),
		};

		SceneEdit.ApplyScene( scene, nonsense );
		scene.ProcessDeletes();

		Assert.AreEqual( 2, scene.Children.Count, "the scene is left as it was" );
	}
}
