using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SceneTests.GameObjects;

//Pins how GameObject references serialize to and from JSON: the reference object form, the legacy guid string form, unknown
//references resolving to null, destroyed objects writing null, and reference fixup through a serialize/clone round trip.
[TestClass]
[DoNotParallelize]
public class GameObjectReferenceTest : SceneTest
{
	//Reads a GameObject from a JSON snippet through the production GameObject.JsonRead entry point.
	static GameObject ReadGameObjectJson( string json )
	{
		var bytes = Encoding.UTF8.GetBytes( json );
		var reader = new Utf8JsonReader( bytes );
		reader.Read();

		return (GameObject)GameObject.JsonRead( ref reader, typeof( GameObject ) );
	}

	//Writes a value through the production GameObject.JsonWrite entry point and returns the produced JSON text.
	static string WriteGameObjectJson( object value )
	{
		using var stream = new MemoryStream();
		using ( var writer = new Utf8JsonWriter( stream ) )
		{
			GameObject.JsonWrite( value, writer );
		}

		return Encoding.UTF8.GetString( stream.ToArray() );
	}

	//The reference object form ({"_type":"gameobject","go":guid}) resolves to the live instance in the active scene.
	[TestMethod]
	public void ReferenceObjectFormResolves()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var resolved = ReadGameObjectJson( $$"""{ "_type": "gameobject", "go": "{{go.Id}}" }""" );

		Assert.AreSame( go, resolved );
	}

	//A reference to a guid that doesn't exist in the scene resolves to null (with a warning) instead of throwing
	//- this happens routinely when deserializing networked data referencing objects we don't know about.
	[TestMethod]
	public void UnknownReferenceResolvesNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var resolved = ReadGameObjectJson( $$"""{ "_type": "gameobject", "go": "{{Guid.NewGuid()}}" }""" );

		Assert.IsNull( resolved );
	}

	//The legacy form - a plain guid string - still resolves to the instance.
	[TestMethod]
	public void LegacyGuidStringResolves()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var resolved = ReadGameObjectJson( $"\"{go.Id}\"" );

		Assert.AreSame( go, resolved );
	}

	//A legacy guid string for an unknown object resolves to null with a warning.
	[TestMethod]
	public void LegacyUnknownGuidStringResolvesNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var resolved = ReadGameObjectJson( $"\"{Guid.NewGuid()}\"" );

		Assert.IsNull( resolved );
	}

	//A reference object with an unknown "_type" is rejected loudly - silent nulls here would hide data corruption.
	[TestMethod]
	public void UnknownReferenceTypeThrows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.ThrowsException<Exception>( () =>
		{
			var bytes = Encoding.UTF8.GetBytes( """{ "_type": "banana" }""" );
			var reader = new Utf8JsonReader( bytes );
			reader.Read();
			GameObject.JsonRead( ref reader, typeof( GameObject ) );
		} );
	}

	//Writing a valid GameObject produces the reference object form containing its guid.
	[TestMethod]
	public void WriteValidGameObjectProducesReference()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var json = WriteGameObjectJson( go );

		StringAssert.Contains( json, "gameobject" );
		StringAssert.Contains( json, go.Id.ToString() );
	}

	//A destroyed GameObject serializes as JSON null, so stale references don't leak invalid guids into saved data.
	[TestMethod]
	public void WriteDestroyedGameObjectIsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.DestroyImmediate();

		Assert.AreEqual( "null", WriteGameObjectJson( go ) );
	}

	//JsonWrite only accepts GameObjects - anything else is a programming error and throws.
	[TestMethod]
	public void WriteNonGameObjectThrows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		Assert.ThrowsException<NotImplementedException>( () => WriteGameObjectJson( 5 ) );
	}

	//A component property referencing a sibling GameObject survives a serialize / make-unique /
	//deserialize round trip: the copy's property points at the copied sibling, not the original.
	[TestMethod]
	public void ComponentPropertyReferenceRemapsOnRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var root = scene.CreateObject();
		var childA = new GameObject( root, true, "A" );
		var childB = new GameObject( root, true, "B" );

		var holder = childB.Components.Create<RefHolderComponent>();
		holder.Target = childA;

		var node = root.Serialize();
		SceneUtility.MakeIdGuidsUnique( node );

		var copy = new GameObject();
		copy.Deserialize( node );

		var copyA = copy.Children.First( x => x.Name == "A" );
		var copyB = copy.Children.First( x => x.Name == "B" );
		var copyHolder = copyB.Components.Get<RefHolderComponent>( true );

		Assert.IsNotNull( copyHolder );
		Assert.AreSame( copyA, copyHolder.Target );
		Assert.AreNotSame( childA, copyHolder.Target );
	}

	//Deserializing a component whose GameObject reference can't be found in the scene leaves the property null instead of failing the whole object.
	[TestMethod]
	public void ComponentPropertyUnknownReferenceBecomesNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var json = Json.ParseToJsonObject( $$"""
			{
				"__guid": "{{Guid.NewGuid()}}",
				"__version": 2,
				"Name": "Holder",
				"Enabled": true,
				"Components": [
					{
						"__type": "{{typeof( RefHolderComponent ).FullName}}",
						"__guid": "{{Guid.NewGuid()}}",
						"Target": { "_type": "gameobject", "go": "{{Guid.NewGuid()}}" }
					}
				]
			}
			""" );

		var go = new GameObject();
		go.Deserialize( json );

		var holder = go.Components.Get<RefHolderComponent>( true );
		Assert.IsNotNull( holder, "the component itself must still deserialize" );
		Assert.IsNull( holder.Target );
	}

	//Serializing a component whose referenced GameObject has been destroyed writes a null value for the property.
	[TestMethod]
	public void DestroyedTargetSerializesAsNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var holder = go.Components.Create<RefHolderComponent>();

		var target = scene.CreateObject();
		holder.Target = target;
		target.DestroyImmediate();

		var node = go.Serialize();
		var compJson = node["Components"].AsArray()[0].AsObject();

		Assert.IsTrue( compJson.ContainsKey( "Target" ) );
		Assert.IsNull( compJson["Target"], "the destroyed reference must serialize as JSON null" );
	}
}

/// <summary>Component with a serialized GameObject reference property, used to test reference resolution. Top level so JSON type lookup by full name works.</summary>
public class RefHolderComponent : Component
{
	/// <summary>The referenced GameObject under test.</summary>
	[Property] public GameObject Target { get; set; }
}
