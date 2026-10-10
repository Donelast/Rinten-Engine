using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rinten.Porting.SBox;

//Replaces MapInstance components with a prefab instance of the level at the scene root
//The old transform becomes an override; edits the scene json directly, no engine needed
internal static class SBoxMapPlacements
{
	private static readonly Logger log = new( "Porter" );

	//What the component was called. The second is its alias, which is what a scene written before they renamed it still says.
	private static readonly HashSet<string> Components = new( StringComparer.OrdinalIgnoreCase )
	{
		"MapInstance",
		"MapComponent",
	};

	/// <summary>Replace every map component in the ported project with the level itself.</summary>
	public static int Place( string assets, PortReport report )
	{
		if ( !Directory.Exists( assets ) ) return 0;

		var placed = 0;

		foreach ( var file in PortPaths.Walk( assets ) )
		{
			var extension = Path.GetExtension( file ).ToLowerInvariant();

			if ( extension is not (".scene" or ".prefab") ) continue;

			var relative = Path.GetRelativePath( assets, file ).Replace( '\\', '/' );

			try
			{
				placed += PlaceInFile( file, relative, assets, report );
			}
			catch ( Exception e )
			{
				report.Skipped.Add( $"{relative}: could not be read to find its map - {e.Message}" );
			}
		}

		return placed;
	}

	//One level about to go into a scene, and where in it.
	private readonly record struct Placement( JsonObject Instance, int After );

	private static int PlaceInFile( string file, string relative, string assets, PortReport report )
	{
		var root = JsonNode.Parse( File.ReadAllText( file ) );
		if ( root is null ) return 0;

		var placed = 0;
		var removed = 0;

		var placements = new List<Placement>();

		//A scene keeps its objects in an array and a prefab keeps its one in RootObject. Either way the walk carries
		//where it is in the world, so an object taken out from under something keeps standing where it stood.
		if ( root["GameObjects"] is JsonArray objects )
		{
			for ( var i = 0; i < objects.Count; i++ )
			{
				if ( objects[i] is JsonObject go )
					Walk( go, Transform.Zero, i, relative, assets, report, placements, ref placed, ref removed );
			}

			//Backwards, so an insert does not move the index of the one after it.
			foreach ( var placement in placements.OrderByDescending( x => x.After ) )
				objects.Insert( placement.After + 1, placement.Instance );
		}
		else if ( root["RootObject"] is JsonObject rootObject )
		{
			Walk( rootObject, Transform.Zero, -1, relative, assets, report, placements, ref placed, ref removed );

			//A prefab has one root and no array to sit beside it in, so the top of the prefab is as far out as a level can go.
			if ( placements.Count > 0 )
			{
				if ( rootObject["Children"] is not JsonArray children )
				{
					children = new JsonArray();
					rootObject["Children"] = children;
				}

				foreach ( var placement in placements ) children.Add( placement.Instance );
			}
		}

		if ( removed == 0 ) return 0;

		File.WriteAllText( file, root.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );

		log.Info( $"scene {relative}: {placed} level(s) placed, {removed} map component(s) removed" );

		return placed;
	}

	//One object and everything under it, carrying where it stands so that a level hung off it can be put at the top without moving.
	private static void Walk( JsonObject go, Transform parent, int top,
		string relative, string assets, PortReport report,
		List<Placement> placements, ref int placed, ref int removed )
	{
		var here = parent.ToWorld( TransformOf( go ) );

		Replace( go, here, top, relative, assets, report, placements, ref placed, ref removed );

		if ( go["Children"] is not JsonArray children ) return;

		//A copy, because a level being placed into a prefab's root adds to this.
		foreach ( var node in children.ToList() )
		{
			if ( node is JsonObject child )
				Walk( child, here, top, relative, assets, report, placements, ref placed, ref removed );
		}
	}

	//Where an object stands, as its own json writes it.
	private static Transform TransformOf( JsonObject go )
	{
		var position = go["Position"] is { } p ? Json.FromNode<Vector3>( p ) : Vector3.Zero;
		var rotation = go["Rotation"] is { } r ? Json.FromNode<Rotation>( r ) : Rotation.Identity;
		var scale = go["Scale"] is { } s ? Json.FromNode<Vector3>( s ) : Vector3.One;

		return new Transform( position, rotation, scale );
	}

	//Take the map component off one object and note the level that belongs where that object is.
	private static void Replace( JsonObject go, Transform at, int top,
		string relative, string assets, PortReport report,
		List<Placement> placements, ref int placed, ref int removed )
	{
		if ( go["Components"] is not JsonArray components ) return;

		//Backwards, because taking one out moves everything after it along.
		for ( var i = components.Count - 1; i >= 0; i-- )
		{
			if ( components[i] is not JsonObject component ) continue;
			if ( !IsMapComponent( component ) ) continue;

			var name = Named( go );
			var named = component["MapName"]?.GetValue<string>();
			var map = Prefab( named, assets, report );

			components.RemoveAt( i );
			++removed;

			if ( map is null )
			{
				//Either it named no map at all, or it named one that is not in this project: a map package off their site, or a level whose file the port
				//never saw. Said out loud, because the component that used to do this is gone and what is left is an empty object where a level was.
				report.Skipped.Add( string.IsNullOrWhiteSpace( named )
					? $"{relative}: '{name}' loaded whatever map the game was launched with, which is not " +
						"something a scene holds here - drop the level's prefab in yourself"
					: $"{relative}: '{name}' named the map '{named}', which is not in this project - " +
						"nothing was put in its place" );

				continue;
			}

			var instance = new JsonObject
			{
				["__guid"] = Guid.NewGuid().ToString(),
				["__Prefab"] = map,

				//Where the object that carried the component stood. Empty when that was the origin, which is a level that is simply the prefab. Both keys
				//have to be here either way - a prefab instance with no patch at all is what the engine logs as an instance whose upgrader never ran.
				["__PrefabInstancePatch"] = Patch( at, map, assets ),
				["__PrefabIdToInstanceId"] = new JsonObject(),
			};

			placements.Add( new Placement( instance, top ) );
			++placed;

			report.Notes.Add( $"{relative}: '{name}' had the map '{named}', which is a prefab at the top of the " +
				"scene now, beside the object that named it" );
		}
	}

	//Instance overrides: empty at the origin, else the old transform (node transforms get replaced)
	private static JsonObject Patch( Transform at, string map, string assets )
	{
		var overrides = new JsonArray();

		if ( !IsOrigin( at ) && RootOf( map, assets ) is { } id )
		{
			void Set( string property, JsonNode value )
			{
				overrides.Add( new JsonObject
				{
					["Target"] = new JsonObject
					{
						["Type"] = "GameObject",
						["IdValue"] = id,
					},
					["Property"] = property,
					["Value"] = value,
				} );
			}

			Set( "Position", Json.ToNode( at.Position ) );
			Set( "Rotation", Json.ToNode( at.Rotation ) );
			Set( "Scale", Json.ToNode( at.Scale ) );
		}

		return new JsonObject
		{
			["AddedObjects"] = new JsonArray(),
			["RemovedObjects"] = new JsonArray(),
			["PropertyOverrides"] = overrides,
			["MovedObjects"] = new JsonArray(),
		};
	}

	//Whether an object stands where a prefab already puts itself.
	private static bool IsOrigin( Transform at )
		=> at.Position.IsNearlyZero() && at.Rotation == Rotation.Identity && at.Scale == Vector3.One;

	//The id of a prefab's own root object, which is what an override on the instance's transform has to name. Null where the file will not read.
	private static string RootOf( string map, string assets )
	{
		try
		{
			var full = Path.Combine( assets, map.Replace( '/', Path.DirectorySeparatorChar ) );

			return JsonNode.Parse( File.ReadAllText( full ) ) is { } prefab
				&& prefab["RootObject"] is JsonObject root
					? root["__guid"]?.GetValue<string>()
					: null;
		}
		catch ( Exception )
		{
			return null;
		}
	}

	//Whether a component in a scene is the one that loaded a map.
	private static bool IsMapComponent( JsonObject component )
	{
		if ( component["__type"]?.GetValue<string>() is not { } type ) return false;

		var dot = type.LastIndexOf( '.' );
		var bare = dot < 0 ? type : type[(dot + 1)..];

		return Components.Contains( bare );
	}

	//Level prefab for a component's map (.vmap or .prefab); port output first, then the file beside it
	private static string Prefab( string named, string assets, PortReport report )
	{
		if ( string.IsNullOrWhiteSpace( named ) ) return null;

		var map = named.Replace( '\\', '/' ).TrimStart( '/' );

		//Keyed by the map, whichever spelling the name is in by now.
		var key = map.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase )
			? Path.ChangeExtension( map, ".vmap" ).Replace( '\\', '/' )
			: map;

		if ( report.Levels.TryGetValue( key, out var written ) ) return written;

		if ( map.EndsWith( ".vmap", StringComparison.OrdinalIgnoreCase ) )
			map = Path.ChangeExtension( map, ".prefab" ).Replace( '\\', '/' );

		//A map package - "org.package" - rather than a file in this project. Nothing was ported for one of those and there is nothing to point at.
		if ( !map.EndsWith( ".prefab", StringComparison.OrdinalIgnoreCase ) ) return null;

		return File.Exists( Path.Combine( assets, map.Replace( '/', Path.DirectorySeparatorChar ) ) ) ? map : null;
	}

	//What to call an object in the report.
	private static string Named( JsonObject go )
	{
		var name = go["Name"]?.GetValue<string>();

		return string.IsNullOrWhiteSpace( name ) ? "an object" : name;
	}
}
