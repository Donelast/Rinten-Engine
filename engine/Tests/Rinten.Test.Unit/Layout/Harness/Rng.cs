using System;

namespace LayoutTests.Harness;

//A small deterministic generator (splitmix64) so a tree built from a seed is the same tree in every engine, every run and every machine.
//The framework's generator is not guaranteed stable across runtime versions, and these seeds are written into committed snapshots.
internal sealed class Rng
{
	private ulong state;

	public Rng( ulong seed )
	{
		state = seed;
	}

	//FNV-1a of a name, for scenarios addressed by name rather than number.
	public static ulong Hash( string name )
	{
		var hash = 14695981039346656037UL;
		foreach ( var c in name ) hash = (hash ^ c) * 1099511628211UL;
		return hash;
	}

	public ulong Next()
	{
		state += 0x9E3779B97F4A7C15UL;
		var z = state;
		z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
		z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
		return z ^ (z >> 31);
	}

	//An integer in [low, high], both ends included.
	public int Between( int low, int high ) => low + (int)(Next() % (ulong)(high - low + 1));

	//True with the given probability in percent.
	public bool Roll( int percent ) => Between( 0, 99 ) < percent;

	public T Pick<T>( params T[] items ) => items[(int)(Next() % (ulong)items.Length)];
}
