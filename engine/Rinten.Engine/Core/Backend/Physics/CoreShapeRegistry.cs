using System.Collections.Concurrent;

namespace Rinten;

//Maps a native shape handle back to the managed PhysicsShape that owns it, for trace and collision results.
//The core keeps the ABI free of managed indices, so the mapping lives here.
internal static class CoreShapeRegistry
{
	private static readonly ConcurrentDictionary<ulong, PhysicsShape> shapes = new();

	public static void Add( ulong handle, PhysicsShape shape )
	{
		if ( handle == 0 || shape is null ) return;
		shapes[handle] = shape;
	}

	public static void Remove( ulong handle )
	{
		if ( handle == 0 ) return;
		shapes.TryRemove( handle, out _ );
	}

	/// <summary>The managed shape for a handle, or null if it has been destroyed.</summary>
	public static PhysicsShape Get( ulong handle )
	{
		if ( handle == 0 ) return null;
		return shapes.TryGetValue( handle, out var shape ) && shape.IsValid() ? shape : null;
	}

	internal static int Count => shapes.Count;
}
