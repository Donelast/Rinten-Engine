using System.Collections.Concurrent;

namespace Rinten;

//Maps a native body handle back to the managed PhysicsBody that owns it. The shape equivalent is
//CoreShapeRegistry; joints need this one because they report the bodies they connect by handle.
internal static class CoreBodyRegistry
{
	private static readonly ConcurrentDictionary<ulong, PhysicsBody> bodies = new();

	public static void Add( ulong handle, PhysicsBody body )
	{
		if ( handle == 0 || body is null ) return;
		bodies[handle] = body;
	}

	public static void Remove( ulong handle )
	{
		if ( handle == 0 ) return;
		bodies.TryRemove( handle, out _ );
	}

	internal static int Count => bodies.Count;

	public static PhysicsBody Get( ulong handle )
	{
		if ( handle == 0 ) return null;
		return bodies.TryGetValue( handle, out var body ) && body.IsValid() ? body : null;
	}
}
