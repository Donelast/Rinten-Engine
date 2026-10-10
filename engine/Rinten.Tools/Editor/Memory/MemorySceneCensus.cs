using Rinten;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Editor;

//One world's worth of scene objects, or one type within it.
internal readonly record struct MemorySceneRow( string Name, long Count, long Delta, int Depth );

//Every scene world and what is in it, by type. Cheap, and the thing that says "the gizmo world
//has grown by two hundred objects a second" before anybody has to go looking at the heap for it.
internal sealed class MemorySceneCensus
{
	//Last census's counts per world and type, for the live delta.
	private readonly Dictionary<string, long> lastCounts = new();

	private readonly Dictionary<string, long> counts = new();
	private readonly Dictionary<Type, long> byType = new();

	private List<MemorySceneRow> rows = new();
	private List<MemorySceneRow> next = new();

	public IReadOnlyList<MemorySceneRow> Rows => rows;

	/// <summary>Walks the worlds again, and says whether anything about the picture changed.</summary>
	public bool Update()
	{
		next.Clear();
		counts.Clear();

		//Worlds have no names and a set has no order, so they are numbered by identity - the same world keeps
		//the same number for as long as it lives, which is what makes the deltas beside them mean anything.
		var worlds = SceneWorld.All.ToArray()
			.Where( x => x is not null )
			.OrderBy( RuntimeHelpers.GetHashCode )
			.ToArray();

		for ( var at = 0; at < worlds.Length; ++at )
		{
			var world = worlds[at];

			byType.Clear();

			long total = 0;

			lock ( world.InternalSceneObjects )
			{
				foreach ( var found in world.InternalSceneObjects )
				{
					//Keyed by the type itself rather than its name: this counts every object in every world
					//once a second, and a reference hash beats a string one when a world holds a million.
					var type = found.GetType();

					byType[type] = byType.GetValueOrDefault( type ) + 1;
					total++;
				}
			}

			if ( total == 0 ) continue;

			var key = RuntimeHelpers.GetHashCode( world );
			var label = $"World {at + 1}{( world.IsTransient ? "" : " (engine)" )}";

			next.Add( Make( $"{key}", label, total, depth: 0 ) );

			foreach ( var (type, count) in byType.OrderByDescending( x => x.Value ) )
				next.Add( Make( $"{key}/{type.FullName}", type.Name, count, depth: 1 ) );
		}

		lastCounts.Clear();

		foreach ( var (key, value) in counts )
			lastCounts[key] = value;

		//Rows compare by value, so nothing moving means nothing to rebuild.
		if ( next.SequenceEqual( rows ) ) return false;

		( rows, next ) = ( next, rows );

		return true;
	}

	private MemorySceneRow Make( string key, string display, long count, int depth )
	{
		counts[key] = count;

		//Something seen for the first time has not grown, it has arrived.
		var delta = count - lastCounts.GetValueOrDefault( key, count );

		return new MemorySceneRow( display, count, delta, depth );
	}
}
