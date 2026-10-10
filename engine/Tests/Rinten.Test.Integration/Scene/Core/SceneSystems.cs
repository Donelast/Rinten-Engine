using System.Text.Json.Nodes;
using Rinten.Internal;

namespace SceneTests.Core;

//Pins how per-scene GameObjectSystem property overrides apply: matching systems get their properties set, unknown systems and malformed json warn
//instead of crashing, and one bad property doesn't stop the others. Transient overrides revert on dispose and are excluded from host scene serialization.
[TestClass]
public class SceneSystemOverridesTest
{
	//Runs the body with a TypeLibrary containing the test assembly, so the test system is instantiated for the scene.
	//The scene is destroyed afterwards so its hooks don't leak - the same convention as GameObjectSystemTests.cs.
	static void WithTestSystems( System.Action<Scene> body )
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Rinten.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( SceneSystemOverridesTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		Scene scene = null;

		try
		{
			scene = new Scene();
			using var sceneScope = scene.Push();
			body( scene );
		}
		finally
		{
			scene?.Destroy();
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	//A valid override sets the system's property.
	[TestMethod]
	public void OverrideApplies()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<OverridableSystem>();
			Assert.IsNotNull( system );
			Assert.AreEqual( 0, system.Speed );

			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			scene.ApplyGameObjectSystemOverrides( node );

			Assert.AreEqual( 42, system.Speed );
		} );
	}

	//Unknown system names are skipped, and a bad value for one property doesn't stop a good value for another.
	[TestMethod]
	public void BadEntriesAreSkipped()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<OverridableSystem>();

			var node = Json.ParseToJsonObject( $$"""
				{
					"Some.System.That.Does.Not.Exist": { "Whatever": 1 },
					"{{typeof( OverridableSystem ).FullName}}": { "Speed": "not a number", "Title": "applied" }
				}
				""" );

			scene.ApplyGameObjectSystemOverrides( node );

			Assert.AreEqual( 0, system.Speed, "the malformed value should be skipped" );
			Assert.AreEqual( "applied", system.Title, "the valid sibling property should still apply" );
		} );
	}

	//Entirely malformed override json warns instead of throwing.
	[TestMethod]
	public void MalformedOverridesNodeIsIgnored()
	{
		WithTestSystems( scene =>
		{
			scene.ApplyGameObjectSystemOverrides( JsonValue.Create( "this is not an object" ) );
			scene.ApplyGameObjectSystemOverrides( null );
		} );
	}

	[TestMethod]
	public void TransientOverrideRevertsOnDispose()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<OverridableSystem>();
			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			using ( scene.ApplyTransientGameObjectSystemOverrides( node ) )
			{
				Assert.AreEqual( 42, system.Speed );
			}

			Assert.AreEqual( 0, system.Speed );
		} );
	}

	[TestMethod]
	public void TransientOverrideIsNotSerialized()
	{
		WithTestSystems( scene =>
		{
			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			using var scope = scene.ApplyTransientGameObjectSystemOverrides( node );

			Assert.AreEqual( 42, scene.GetSystem<OverridableSystem>().Speed );
			Assert.IsNull( SerializedSystemSpeed( scene ) );
		} );
	}

	[TestMethod]
	public void NonTransientOverrideIsSerialized()
	{
		WithTestSystems( scene =>
		{
			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			scene.ApplyGameObjectSystemOverrides( node );

			Assert.AreEqual( 42, (int)SerializedSystemSpeed( scene ) );
		} );
	}

	[TestMethod]
	public void TransientOverridePreservesSceneOwnValue()
	{
		WithTestSystems( scene =>
		{
			var sceneNode = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 7 } }
				""" );
			scene.ApplyGameObjectSystemOverrides( sceneNode );

			var mapNode = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			using ( scene.ApplyTransientGameObjectSystemOverrides( mapNode ) )
			{
				Assert.AreEqual( 42, scene.GetSystem<OverridableSystem>().Speed );
				Assert.AreEqual( 7, (int)SerializedSystemSpeed( scene ) );
			}

			Assert.AreEqual( 7, scene.GetSystem<OverridableSystem>().Speed );
			Assert.AreEqual( 7, (int)SerializedSystemSpeed( scene ) );
		} );
	}

	static JsonNode SerializedSystemSpeed( Scene scene )
	{
		if ( scene.SerializeProperties()["GameObjectSystems"] is not JsonObject systems )
			return null;

		if ( systems[typeof( OverridableSystem ).FullName] is not JsonObject properties )
			return null;

		return properties["Speed"];
	}

	/// <summary>A property-holding scene system used to test overrides. Inert - it has no behavior, so its presence in other tests' scenes is harmless.</summary>
	public class OverridableSystem : GameObjectSystem
	{
		[Property] public int Speed { get; set; }
		[Property] public string Title { get; set; }

		public OverridableSystem( Scene scene ) : base( scene )
		{
		}
	}
}
