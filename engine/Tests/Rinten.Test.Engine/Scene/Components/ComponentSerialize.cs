using System;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SceneTests.Components;

//Tests for Component.Serialize.cs - what a component writes into JSON, which flags
//suppress serialization entirely, and how deserialization applies (or preserves) values.
[TestClass]
[DoNotParallelize]
public class ComponentSerializeTest : SceneTest
{
	//Serialize writes the type/id/enabled/flags header and every [Property] member - including public fields
	//and private setters - while skipping [JsonIgnore] members and plain properties without [Property].
	[TestMethod]
	public void SerializeWritesHeaderAndProperties()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SerializeDataComponent>();

		comp.Number = 5;
		comp.Text = "hello";
		comp.Position = new Vector3( 1, 2, 3 );
		comp.Counter = 9;
		comp.SetSecret( 42 );
		comp.Ignored = 4;
		comp.NotAProperty = 4;

		var json = (JsonObject)comp.Serialize();

		StringAssert.Contains( (string)json["__type"], "SerializeDataComponent" );
		Assert.AreEqual( comp.Id, (Guid)json["__guid"] );
		Assert.IsTrue( (bool)json["__enabled"] );

		Assert.AreEqual( 5, (int)json["Number"] );
		Assert.AreEqual( "hello", (string)json["Text"] );
		Assert.AreEqual( 9, (int)json["Counter"] );
		Assert.AreEqual( 42, (int)json["Secret"] );

		Assert.IsFalse( json.ContainsKey( "Ignored" ) );
		Assert.IsFalse( json.ContainsKey( "NotAProperty" ) );
	}

	//Components flagged NotSaved or NotCloned serialize to null - they are omitted entirely from saved scenes and clones.
	[TestMethod]
	public void SerializeReturnsNullForSuppressedFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();

		var notSaved = go.Components.Create<SerializeDataComponent>();
		notSaved.Flags |= ComponentFlags.NotSaved;
		Assert.IsNull( notSaved.Serialize() );

		var notCloned = go.Components.Create<SerializeDataComponent>();
		notCloned.Flags |= ComponentFlags.NotCloned;
		Assert.IsNull( notCloned.Serialize() );
	}

	//With SkipNulls the serializer omits null reference properties instead of writing explicit nulls; without it the key is present.
	[TestMethod]
	public void SerializeSkipNullsOmitsNullProperties()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SerializeDataComponent>();
		comp.Text = null;
		comp.Number = 5;

		var plain = (JsonObject)comp.Serialize();
		Assert.IsTrue( plain.ContainsKey( "Text" ) );

		var skipped = (JsonObject)comp.Serialize( new GameObject.SerializeOptions { SkipNulls = true } );
		Assert.IsFalse( skipped.ContainsKey( "Text" ) );
		Assert.AreEqual( 5, (int)skipped["Number"] );
	}

	//A serialized component can be deserialized into a fresh instance, restoring its id, enabled
	//state and every [Property] member - including public fields and properties with private setters.
	[TestMethod]
	public void RoundTripRestoresProperties()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SerializeDataComponent>();

		comp.Number = 5;
		comp.Text = "hello";
		comp.Position = new Vector3( 1, 2, 3 );
		comp.Counter = 9;
		comp.SetSecret( 42 );

		var originalId = comp.Id;
		var json = (JsonObject)comp.Serialize();

		//free the id so the deserialized copy can claim it
		comp.Destroy();

		var go2 = scene.CreateObject();
		var copy = go2.Components.Create<SerializeDataComponent>( false );
		copy.DeserializeImmediately( json );

		Assert.AreEqual( originalId, copy.Id );
		Assert.IsTrue( copy.Enabled );
		Assert.AreEqual( 5, copy.Number );
		Assert.AreEqual( "hello", copy.Text );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), copy.Position );
		Assert.AreEqual( 9, copy.Counter );
		Assert.AreEqual( 42, copy.Secret );
	}

	//Properties absent from the JSON are left at their current value - older save files must not clobber newer code-defined defaults.
	[TestMethod]
	public void DeserializePreservesValuesForAbsentKeys()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SerializeDataComponent>();
		comp.Text = "from json";

		var json = (JsonObject)comp.Serialize();
		json.Remove( "Number" );

		comp.Destroy();

		var go2 = scene.CreateObject();
		var copy = go2.Components.Create<SerializeDataComponent>( false );
		copy.Number = 42;
		copy.DeserializeImmediately( json );

		Assert.AreEqual( 42, copy.Number );
		Assert.AreEqual( "from json", copy.Text );
	}

	//A disabled component round-trips its enabled state - the deserialized copy comes back disabled.
	[TestMethod]
	public void DeserializeAppliesEnabledState()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SerializeDataComponent>( false );

		var json = (JsonObject)comp.Serialize();
		Assert.IsFalse( (bool)json["__enabled"] );

		comp.Destroy();

		var go2 = scene.CreateObject();
		var copy = go2.Components.Create<SerializeDataComponent>( false );
		copy.DeserializeImmediately( json );

		Assert.IsFalse( copy.Enabled );
		Assert.IsFalse( copy.Active );
	}

	//One property setter throwing during deserialization must not abort the whole component - the remaining properties are still applied.
	[TestMethod]
	public void DeserializeSurvivesThrowingSetter()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<ThrowingSetterComponent>();

		var json = (JsonObject)comp.Serialize();
		json["Bad"] = 5;
		json["Good"] = 7;

		comp.Destroy();

		var go2 = scene.CreateObject();
		var copy = go2.Components.Create<ThrowingSetterComponent>( false );
		copy.DeserializeImmediately( json );

		Assert.AreEqual( 7, copy.Good );
	}

	//Only whitelisted flags survive a round trip: ShowAdvancedProperties is restored from JSON, transient flags like Hidden are not.
	[TestMethod]
	public void FlagsRoundTripOnlySavedFlags()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<SerializeDataComponent>();
		comp.Flags |= ComponentFlags.ShowAdvancedProperties | ComponentFlags.Hidden;

		var json = (JsonObject)comp.Serialize();
		comp.Destroy();

		var go2 = scene.CreateObject();
		var copy = go2.Components.Create<SerializeDataComponent>( false );
		copy.DeserializeImmediately( json );

		Assert.IsTrue( copy.Flags.HasFlag( ComponentFlags.ShowAdvancedProperties ) );
		Assert.IsFalse( copy.Flags.HasFlag( ComponentFlags.Hidden ) );
	}
}

/// <summary>Component covering the serializable member shapes: plain properties, a public field, a private setter, a [JsonIgnore] property and a property without [Property].</summary>
public class SerializeDataComponent : Component
{
	[Property]
	public int Number { get; set; }

	[Property]
	public string Text { get; set; }

	[Property]
	public Vector3 Position { get; set; }

	[Property]
	public int Counter;

	[Property]
	public int Secret { get; private set; }

	[Property, JsonIgnore]
	public int Ignored { get; set; }

	public int NotAProperty { get; set; }

	//Sets the private-setter property from test code.
	public void SetSecret( int value ) => Secret = value;
}

/// <summary>Component whose Bad property throws from its setter, to prove deserialization isolates per-property failures.</summary>
public class ThrowingSetterComponent : Component
{
	[Property]
	public int Bad { get => 0; set => throw new InvalidOperationException( "setter exploded" ); }

	[Property]
	public int Good { get; set; }
}
