using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Editor;

//A scene edit record holding the resulting state of touched objects, so applying it twice is safe
internal static class SceneEdit
{
	//In the order the editor's own undo does it: what went, goes; what is missing is made, so that references to it resolve; and only then is everything deserialised and put back in the right place in the tree.
	/// <summary>Applies one record to a scene.</summary>
	public static void Apply( Scene scene, JsonObject record )
	{
		if ( scene is null || record is null ) return;

		using var blobs = BlobDataSerializer.LoadFromMemory( default );
		using var batch = CallbackBatch.Batch();

		Remove( scene, record["removedComponents"] as JsonArray, isGameObject: false );
		Remove( scene, record["removedGameObjects"] as JsonArray, isGameObject: true );

		var gameObjects = record["gameObjects"] as JsonArray;
		var components = record["components"] as JsonArray;

		//First pass: everything the record mentions exists, even if it is empty. A component being deserialised may refer to an object later in the list.
		var made = new List<(GameObject Object, JsonObject Entry)>();

		if ( gameObjects is not null )
		{
			foreach ( var entry in gameObjects )
			{
				if ( entry is not JsonObject item ) continue;

				var id = Id( item["id"] );

				if ( id == Guid.Empty ) continue;

				var state = item["state"] as JsonObject;

				if ( state is null ) continue;

				var go = scene.Directory.FindByGuid( id );

				if ( !go.IsValid() )
				{
					go = new GameObject();
					go.DeserializeId( state );
				}

				made.Add( (go, item) );
			}
		}

		//Second pass: the contents, then where they sit.
		foreach ( var (go, item) in made )
		{
			var state = item["state"] as JsonObject;

			blobs.LoadFrom( state );

			go.Deserialize( state, new GameObject.DeserializeOptions { IsRefreshing = true } );
		}

		foreach ( var (go, item) in made )
		{
			var parent = scene.Directory.FindByGuid( Id( item["parent"] ) );
			var sibling = scene.Directory.FindByGuid( Id( item["nextSibling"] ) );

			if ( sibling.IsValid() ) sibling.AddSibling( go, true, false );
			else if ( parent.IsValid() ) go.SetParent( parent, false );
			else go.SetParent( scene );
		}

		if ( components is null ) return;

		foreach ( var entry in components )
		{
			if ( entry is not JsonObject item ) continue;

			var state = item["state"] as JsonObject;

			if ( state is null ) continue;

			var component = scene.Directory.FindComponentByGuid( Id( item["id"] ) );

			//A component that is not there is one its game object's own state brings back - components are
			//serialised inside the object they are on, and every record that creates one carries that object too.
			if ( !component.IsValid() ) continue;

			blobs.LoadFrom( state );

			component.Deserialize( state );
		}
	}

	/// <summary>Puts a whole scene back the way a snapshot has it, without emptying it first.</summary>
	public static void ApplyScene( Scene scene, JsonObject snapshot )
	{
		if ( scene is null || snapshot is null ) return;

		if ( snapshot["GameObjects"] is not JsonArray objects ) return;

		using var blobs = BlobDataSerializer.LoadFrom( snapshot );
		using var batch = CallbackBatch.Batch();

		var wanted = new HashSet<Guid>();
		var order = new List<GameObject>();

		foreach ( var entry in objects )
		{
			if ( entry is not JsonObject state ) continue;

			//"__guid", which is what a serialised object calls its id. Reading the wrong key here would mean
			//nothing in the snapshot is recognised and everything in the scene looks like it was added afterwards.
			var id = Id( state["__guid"] );

			if ( id == Guid.Empty ) continue;

			wanted.Add( id );

			var go = scene.Directory.FindByGuid( id );

			if ( !go.IsValid() )
			{
				go = new GameObject( false );
				go.DeserializeId( state );
				go.Parent = scene;
			}

			order.Add( go );
		}

		//A snapshot nothing was recognised in is one this does not understand, and the next thing it would do is empty the scene. Left alone instead, loudly.
		if ( wanted.Count == 0 )
		{
			Log.Warning( $"a scene snapshot with {objects.Count} objects in it had no ids this could read - " +
				"the scene has been left as it is" );

			return;
		}

		//Anything the snapshot does not have was made after it - which, for an undo, is what is being taken back.
		foreach ( var go in scene.Children.ToArray() )
		{
			if ( !go.IsValid() ) continue;
			if ( wanted.Contains( go.Id ) ) continue;

			go.Destroy();
		}

		var i = 0;

		foreach ( var entry in objects )
		{
			if ( entry is not JsonObject state ) continue;
			if ( i >= order.Count ) break;

			var go = order[i++];

			if ( !go.IsValid() ) continue;

			blobs.LoadFrom( state );

			go.Deserialize( state, new GameObject.DeserializeOptions { IsRefreshing = true } );
		}

		//And in the order the snapshot has them, which is the order the tree shows.
		GameObject previous = null;

		foreach ( var go in order )
		{
			if ( !go.IsValid() ) continue;

			if ( previous is null ) go.SetParent( scene );
			else previous.AddSibling( go, false, false );

			previous = go;
		}
	}

	/// <summary>Builds one side of the record for an edit out of what the undo scope captured.</summary>
	public static JsonObject Record( string name )
	{
		return new JsonObject
		{
			["name"] = name ?? string.Empty,
			["gameObjects"] = new JsonArray(),
			["components"] = new JsonArray(),
			["removedGameObjects"] = new JsonArray(),
			["removedComponents"] = new JsonArray(),
		};
	}

	/// <summary>The two sides of one edit, as they are written into the history.</summary>
	public static JsonObject Both( string name, JsonObject after, JsonObject before )
	{
		return new JsonObject
		{
			["name"] = name ?? string.Empty,
			["after"] = after,
			["before"] = before,
		};
	}

	//A journal written before there were two sides holds the "after" side at the top level, so a record without them is read as that. Nobody's history is thrown away for being a version behind.
	/// <summary>One side of a record, by name, whichever way round it was written.</summary>
	public static JsonObject Side( JsonObject record, bool after )
	{
		if ( record is null ) return null;

		if ( record["after"] is JsonObject or null && record["before"] is JsonObject or null
			&& (record["after"] is not null || record["before"] is not null) )
		{
			return record[after ? "after" : "before"] as JsonObject;
		}

		return after ? record : null;
	}

	/// <summary>A mark saying the document on the disk holds every edit before this point.</summary>
	public static JsonObject Checkpoint( ulong document )
	{
		return new JsonObject
		{
			["kind"] = "checkpoint",
			["document"] = document.ToString(),
		};
	}

	/// <summary>Whether a record is one of those marks.</summary>
	public static bool IsCheckpoint( JsonObject record ) => record?["kind"]?.ToString() == "checkpoint";

	/// <summary>Which version of the document a mark says was written.</summary>
	public static ulong CheckpointDocument( JsonObject record )
	{
		return ulong.TryParse( record?["document"]?.ToString(), out var hash ) ? hash : 0;
	}

	/// <summary>What the edit is called, as the undo menu says it.</summary>
	public static string NameOf( JsonObject record ) => record?["name"]?.ToString() ?? "Edit";

	/// <summary>Adds a game object's state, where it sits and what it sits after.</summary>
	public static void AddGameObject( JsonObject record, Guid id, Guid parent, Guid nextSibling, JsonObject state )
	{
		if ( state is null ) return;

		(record["gameObjects"] as JsonArray)?.Add( new JsonObject
		{
			["id"] = id.ToString(),
			["parent"] = parent.ToString(),
			["nextSibling"] = nextSibling.ToString(),
			["state"] = state.DeepClone(),
		} );
	}

	/// <summary>Adds a component's state.</summary>
	public static void AddComponent( JsonObject record, Guid id, JsonObject state )
	{
		if ( state is null ) return;

		(record["components"] as JsonArray)?.Add( new JsonObject
		{
			["id"] = id.ToString(),
			["state"] = state.DeepClone(),
		} );
	}

	/// <summary>Says that something is no longer in the scene.</summary>
	public static void AddRemoved( JsonObject record, Guid id, bool isGameObject )
	{
		var into = record[isGameObject ? "removedGameObjects" : "removedComponents"] as JsonArray;

		into?.Add( id.ToString() );
	}

	/// <summary>Whether the record would change anything at all.</summary>
	public static bool IsEmpty( JsonObject record )
	{
		if ( record is null ) return true;

		return Count( record, "gameObjects" ) == 0
			&& Count( record, "components" ) == 0
			&& Count( record, "removedGameObjects" ) == 0
			&& Count( record, "removedComponents" ) == 0;
	}

	private static int Count( JsonObject record, string name ) => (record[name] as JsonArray)?.Count ?? 0;

	private static void Remove( Scene scene, JsonArray ids, bool isGameObject )
	{
		if ( ids is null ) return;

		foreach ( var node in ids )
		{
			var id = Id( node );

			if ( id == Guid.Empty ) continue;

			if ( isGameObject ) scene.Directory.FindByGuid( id )?.Destroy();
			else scene.Directory.FindComponentByGuid( id )?.Destroy();
		}
	}

	//Reads a guid from json whether stored as a string or a guid value
	private static Guid Id( JsonNode node )
	{
		if ( node is null ) return Guid.Empty;

		try
		{
			return node.Deserialize<Guid>();
		}
		catch ( System.Exception )
		{
			return Guid.TryParse( node.ToString().Trim( '"' ), out var id ) ? id : Guid.Empty;
		}
	}
}
