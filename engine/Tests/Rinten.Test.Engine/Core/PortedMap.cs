using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using Rinten.Porting;
using Rinten.Porting.SBox;
using TestFixtures;

namespace EngineTests;

//Reads binary .vmap maps; brushwork is tested in Rinten.Test.Integration (needs material system)
[TestClass]
public class PortedMapTest
{
	private static string TempDirectory()
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-map-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( path );
		return path;
	}

	//Every value type reads back; lone strings index the table, strings in arrays are inline
	[TestMethod]
	public void ReadsTheTreeOffTheBytes()
	{
		var document = SBoxMapDocument.Read( HammerMap.Build() );

		var world = document.Root?.GetElement( "world" );

		Assert.IsNotNull( world, "the root should point at the world" );
		Assert.AreEqual( "CMapWorld", world.TypeName );

		var children = world.GetElements( "children" );
		Assert.AreEqual( 2, children.Count, "a brush and a prop" );

		var brush = children[0];
		Assert.AreEqual( "CMapMesh", brush.TypeName );
		Assert.AreEqual( new Vector3( 128, 0, 64 ), brush.GetVector3( "origin" ), "vectors are three floats where they stand" );
		Assert.AreEqual( 40.0f, brush.GetFloat( "smoothingAngle" ), 0.001f );
		Assert.AreEqual( "default", brush.GetString( "physicsType" ), "a lone string is an index into the table" );

		var data = brush.GetElement( "meshData" );
		Assert.IsNotNull( data, "an element attribute points by index" );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 0 }, data.GetInts( "edgeNextIndices" ) );

		//The one that has to be written out in full rather than indexed.
		var stream = data.GetElement( "vertexData" ).GetElements( "streams" )[0];
		Assert.AreEqual( "position:0", stream.Name, "a stream is named on the element, not in an attribute" );
		Assert.AreEqual( 4, stream.GetVector3s( "data" ).Length );

		var properties = children[1].GetElement( "entity_properties" );
		Assert.AreEqual( "prop_static", properties.GetString( "classname" ) );
	}

	//A map's entities come out under the prefab's root object, in metres.
	[TestMethod]
	public void PortsEntitiesIntoAPrefab()
	{
		var source = TempDirectory();
		var destination = TempDirectory();

		try
		{
			var map = Path.Combine( source, "maps", "test.vmap" );
			Directory.CreateDirectory( Path.GetDirectoryName( map ) );
			File.WriteAllBytes( map, HammerMap.Build( withBrush: false ) );

			var report = new PortReport();

			Assert.IsTrue( SBoxMaps.Port( map, Path.Combine( "maps", "test.vmap" ), source, destination, report ),
				$"the map should have been read - {string.Join( "; ", report.Skipped )}" );

			var written = Path.Combine( destination, "maps", "test.prefab" );
			Assert.IsTrue( File.Exists( written ), "a prefab should have been written where the map was" );

			var prefab = JsonNode.Parse( File.ReadAllText( written ) ) as JsonObject;
			var rootObject = prefab["RootObject"] as JsonObject;

			//One object, at the origin, with the level under it - so dropping it into a scene puts the level where it was dropped.
			Assert.IsNotNull( rootObject, "a prefab is one object" );
			Assert.AreEqual( "test", rootObject["Name"].GetValue<string>(), "named after the map" );
			Assert.IsNull( rootObject["Position"], "and sitting at the origin" );

			var objects = rootObject["Children"] as JsonArray;

			Assert.AreEqual( 1, objects.Count, "the one prop" );

			var prop = objects[0] as JsonObject;
			Assert.AreEqual( "chair", prop["Name"].GetValue<string>(), "named after the model it draws" );

			//The map's 32 inches, which is 0.8128 metres. A level left in inches is
			//the failure that looks like nothing is wrong until somebody stands next to it.
			var at = Json.FromNode<Vector3>( prop["Position"] );
			Assert.AreEqual( 0.8128f, at.y, 0.0001f, "and where it was, in metres" );
			Assert.AreEqual( 0.0f, at.x, 0.0001f );

			var components = prop["Components"] as JsonArray;
			Assert.AreEqual( 2, components.Count, "something to draw it and something to hit it with" );
			Assert.AreEqual( "Rinten.ModelRenderer", ( components[0] as JsonObject )["__type"].GetValue<string>() );
			Assert.AreEqual( "models/chair.vmdl", ( components[0] as JsonObject )["Model"].GetValue<string>() );
			Assert.AreEqual( "Rinten.ModelCollider", ( components[1] as JsonObject )["__type"].GetValue<string>() );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}

	//A map this cannot make sense of is reported, not thrown out of the port. One unreadable map must not take the rest of the project with
	//it. A port has hundreds of files to get through and whoever ran it needs all of them, plus a line saying which one it could not read.
	[TestMethod]
	public void SaysSoRatherThanThrowing()
	{
		var source = TempDirectory();
		var destination = TempDirectory();

		try
		{
			var map = Path.Combine( source, "broken.vmap" );
			File.WriteAllBytes( map, Encoding.ASCII.GetBytes( "not a map at all" ) );

			var report = new PortReport();

			Assert.IsFalse( SBoxMaps.Port( map, "broken.vmap", source, destination, report ) );
			Assert.AreEqual( 1, report.Skipped.Count, "and it should say which map, and why" );
			Assert.IsTrue( report.Skipped[0].Contains( "broken.vmap" ) );
		}
		finally
		{
			Directory.Delete( source, true );
			Directory.Delete( destination, true );
		}
	}

	//A scene that named a map through their map component ends up with the level standing in it. The whole point of porting a map. A level that
	//arrives as a prefab nobody put anywhere is a level that is not in the game, and the scene it belongs in is the one thing the project already says.
	[TestMethod]
	public void PutsTheLevelWhereTheMapComponentWas()
	{
		var assets = TempDirectory();

		try
		{
			Directory.CreateDirectory( Path.Combine( assets, "maps" ) );
			File.WriteAllText( Path.Combine( assets, "maps", "test.prefab" ), "{}" );

			var scene = Path.Combine( assets, "main.scene" );
			File.WriteAllText( scene, SceneNaming( "maps/test.prefab" ) );

			var report = new PortReport();

			Assert.AreEqual( 1, SBoxMapPlacements.Place( assets, report ),
				$"the level should have been placed - {string.Join( "; ", report.Skipped )}" );

			var written = JsonNode.Parse( File.ReadAllText( scene ) ) as JsonObject;
			var objects = written["GameObjects"] as JsonArray;
			var host = objects[0] as JsonObject;

			//The object it was on is still the object it was on: its name, its place and anything else it carried.
			Assert.AreEqual( "Map", host["Name"].GetValue<string>() );
			Assert.AreEqual( 0, ( host["Components"] as JsonArray ).Count, "and the component is gone" );

			//The level is at the top of the scene beside it, not inside it: a level is the scene rather than a thing hanging off an empty.
			Assert.AreEqual( 2, objects.Count, "the object it was on, and the level next to it" );

			var instance = objects[1] as JsonObject;

			Assert.AreEqual( "maps/test.prefab", instance["__Prefab"].GetValue<string>() );
			Assert.IsNotNull( instance["__guid"], "an instance needs an id of its own" );

			//An instance with no patch at all is what the engine reads as one whose upgrader never ran: it logs an error and gives up on it.
			var patch = instance["__PrefabInstancePatch"] as JsonObject;

			Assert.IsNotNull( patch, "and an empty patch, which is not the same as none" );
			Assert.AreEqual( 0, ( patch["PropertyOverrides"] as JsonArray ).Count );
			Assert.IsNotNull( instance["__PrefabIdToInstanceId"], "and a lookup for the engine to fill in" );
		}
		finally
		{
			Directory.Delete( assets, true );
		}
	}

	//A map with no ported file drops the component and logs which scene lost what
	[TestMethod]
	public void SaysSoWhenThereIsNoLevelToPlace()
	{
		var assets = TempDirectory();

		try
		{
			var scene = Path.Combine( assets, "main.scene" );
			File.WriteAllText( scene, SceneNaming( "facepunch.construct" ) );

			var report = new PortReport();

			Assert.AreEqual( 0, SBoxMapPlacements.Place( assets, report ), "there is no prefab to place" );

			var written = JsonNode.Parse( File.ReadAllText( scene ) ) as JsonObject;
			var host = ( written["GameObjects"] as JsonArray )[0] as JsonObject;

			Assert.AreEqual( 0, ( host["Components"] as JsonArray ).Count,
				"the component goes anyway - nothing here reads it" );

			Assert.AreEqual( 1, report.Skipped.Count );
			Assert.IsTrue( report.Skipped[0].Contains( "facepunch.construct" ),
				$"and it should say what was named: {report.Skipped[0]}" );
		}
		finally
		{
			Directory.Delete( assets, true );
		}
	}

	//When the map name was taken by a prefab, the level saved under another name is still placed
	[TestMethod]
	public void PlacesTheLevelEvenWhenItsNameWasTaken()
	{
		var assets = TempDirectory();

		try
		{
			Directory.CreateDirectory( Path.Combine( assets, "maps" ) );

			//Somebody's own prefab, sitting where the level's name would have put it
			File.WriteAllText( Path.Combine( assets, "maps", "test.prefab" ), "{}" );
			File.WriteAllText( Path.Combine( assets, "maps", "test_map.prefab" ), "{}" );

			var scene = Path.Combine( assets, "main.scene" );
			File.WriteAllText( scene, SceneNaming( "maps/test.prefab" ) );

			var report = new PortReport();
			report.Levels["maps/test.vmap"] = "maps/test_map.prefab";

			Assert.AreEqual( 1, SBoxMapPlacements.Place( assets, report ) );

			var written = JsonNode.Parse( File.ReadAllText( scene ) ) as JsonObject;
			var instance = ( written["GameObjects"] as JsonArray )[1] as JsonObject;

			Assert.AreEqual( "maps/test_map.prefab", instance["__Prefab"].GetValue<string>(),
				"the level the port wrote, not the prefab that took its name" );
		}
		finally
		{
			Directory.Delete( assets, true );
		}
	}

	//The level keeps its old object's position instead of landing at the origin
	[TestMethod]
	public void TheLevelKeepsWhereItsObjectStood()
	{
		var assets = TempDirectory();

		try
		{
			Directory.CreateDirectory( Path.Combine( assets, "maps" ) );

			File.WriteAllText( Path.Combine( assets, "maps", "test.prefab" ), """
			{
				"RootObject": { "__guid": "11111111-2222-3333-4444-555555555555", "Name": "test" }
			}
			""" );

			var scene = Path.Combine( assets, "main.scene" );
			File.WriteAllText( scene, SceneNaming( "maps/test.prefab", position: "0,1.28,0" ) );

			var report = new PortReport();

			Assert.AreEqual( 1, SBoxMapPlacements.Place( assets, report ) );

			var written = JsonNode.Parse( File.ReadAllText( scene ) ) as JsonObject;
			var instance = ( written["GameObjects"] as JsonArray )[1] as JsonObject;

			var overrides = instance["__PrefabInstancePatch"]["PropertyOverrides"] as JsonArray;

			Assert.AreEqual( 3, overrides.Count, "where it is, which way it faces and how big it is" );

			var first = overrides[0] as JsonObject;

			Assert.AreEqual( "GameObject", first["Target"]["Type"].GetValue<string>() );
			Assert.AreEqual( "11111111-2222-3333-4444-555555555555", first["Target"]["IdValue"].GetValue<string>(),
				"the prefab's own root is what an override has to name" );
			Assert.AreEqual( "Position", first["Property"].GetValue<string>() );

			var at = Json.FromNode<Vector3>( first["Value"] );
			Assert.AreEqual( 1.28f, at.y, 0.0001f, "exactly where the object it came out of stood" );
		}
		finally
		{
			Directory.Delete( assets, true );
		}
	}

	//A scene with one object carrying their map component.
	private static string SceneNaming( string mapName, string position = "0,0,0" )
	{
		return $$"""
		{
			"GameObjects": [
				{
					"__guid": "8a0d2e6c-1f28-4a6e-9d4f-3c2b1a0e5d77",
					"Name": "Map",
					"Position": "{{position}}",
					"Components": [
						{
							"__type": "Rinten.MapInstance",
							"__guid": "b1c3d5e7-2a4b-4c6d-8e0f-1a2b3c4d5e6f",
							"MapName": "{{mapName}}"
						}
					]
				}
			]
		}
		""";
	}
}
