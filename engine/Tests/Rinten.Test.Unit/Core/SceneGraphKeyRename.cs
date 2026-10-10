using System.Text.Json.Nodes;

namespace UnitTests.Core;

//An upgrader renames the old AnimationGraph2 key to AnimGraph, or old scenes lose their graphs
[TestClass]
public class SceneGraphKeyRenameTests
{
	private static JsonObject Renderer( string key, string value )
		=> new()
		{
			["__type"] = "Rinten.SkinnedModelRenderer",
			[key] = value,
		};

	private static JsonObject SceneWith( params JsonObject[] components )
	{
		var list = new JsonArray();
		foreach ( var c in components ) list.Add( c );

		return new JsonObject
		{
			["GameObjects"] = new JsonArray { new JsonObject { ["Components"] = list } },
		};
	}

	[TestMethod]
	public void TheOldKeyBecomesTheNewOne()
	{
		var scene = SceneWith( Renderer( "AnimationGraph2", "animgraphs/citizen.ag" ) );

		Rinten.SceneFile.RenameGraphKey( scene );

		var renderer = scene["GameObjects"][0]["Components"][0].AsObject();

		Assert.IsFalse( renderer.ContainsKey( "AnimationGraph2" ), "the old key is gone" );
		Assert.AreEqual( "animgraphs/citizen.ag", (string)renderer["AnimGraph"],
			"and the graph it named is on the new key, not lost" );
	}

	[TestMethod]
	public void AGraphNamedUnderTheNewKeyIsLeftAlone()
	{
		var scene = SceneWith( Renderer( "AnimGraph", "animgraphs/new.ag" ) );

		Rinten.SceneFile.RenameGraphKey( scene );

		var renderer = scene["GameObjects"][0]["Components"][0].AsObject();

		Assert.AreEqual( "animgraphs/new.ag", (string)renderer["AnimGraph"] );
	}

	//Both keys present is a scene half-written by each version. What the new name says wins, because that is what the renderer last played.
	[TestMethod]
	public void TheNewKeyWinsWhereBothAreWritten()
	{
		var renderer = Renderer( "AnimGraph", "animgraphs/new.ag" );
		renderer["AnimationGraph2"] = "animgraphs/old.ag";

		var scene = SceneWith( renderer );

		Rinten.SceneFile.RenameGraphKey( scene );

		var read = scene["GameObjects"][0]["Components"][0].AsObject();

		Assert.IsFalse( read.ContainsKey( "AnimationGraph2" ) );
		Assert.AreEqual( "animgraphs/new.ag", (string)read["AnimGraph"] );
	}

	//A character usually keeps its renderer on a prefab, and a prefab nests its objects rather than listing them.
	[TestMethod]
	public void ARendererNestedInAPrefabIsReachedToo()
	{
		var prefab = new JsonObject
		{
			["RootObject"] = new JsonObject
			{
				["Children"] = new JsonArray
				{
					new JsonObject
					{
						["Components"] = new JsonArray { Renderer( "AnimationGraph2", "animgraphs/deep.ag" ) },
					},
				},
			},
		};

		Rinten.SceneFile.RenameGraphKey( prefab );

		var renderer = prefab["RootObject"]["Children"][0]["Components"][0].AsObject();

		Assert.AreEqual( "animgraphs/deep.ag", (string)renderer["AnimGraph"] );
	}

	//An empty graph field is still a field, and stays one.
	[TestMethod]
	public void ARendererThatNamedNoGraphKeepsSayingSo()
	{
		var renderer = new JsonObject
		{
			["__type"] = "Rinten.SkinnedModelRenderer",
			["AnimationGraph2"] = null,
		};

		var scene = SceneWith( renderer );

		Rinten.SceneFile.RenameGraphKey( scene );

		var read = scene["GameObjects"][0]["Components"][0].AsObject();

		Assert.IsFalse( read.ContainsKey( "AnimationGraph2" ) );
		Assert.IsTrue( read.ContainsKey( "AnimGraph" ) );
		Assert.IsNull( read["AnimGraph"] );
	}

	//A scene with nothing to rename comes through untouched.
	[TestMethod]
	public void AnythingElseIsNotDisturbed()
	{
		var other = new JsonObject
		{
			["__type"] = "Rinten.ModelRenderer",
			["Model"] = "models/thing.vmdl",
		};

		var scene = SceneWith( other );

		Rinten.SceneFile.RenameGraphKey( scene );

		var read = scene["GameObjects"][0]["Components"][0].AsObject();

		Assert.AreEqual( "models/thing.vmdl", (string)read["Model"] );
		Assert.IsFalse( read.ContainsKey( "AnimGraph" ) );
	}
}
