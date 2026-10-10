namespace Rinten;

//Handle-indexed table: handles start at one and are never reused, so lookup is a bounds check
internal sealed class VkHandleTable<T> where T : struct
{
	private T[] items = new T[64];
	private bool[] live = new bool[64];
	private ulong next = 1;

	//Past the last handle ever handed out - what Live and At are bounded by.
	internal int Used => (int)next;

	//Whether a slot names something. The index is the handle.
	internal bool Live( int at ) => (uint)at < (uint)live.Length && live[at];

	//What a slot names, by reference, for a caller that has checked Live.
	internal ref T At( int at ) => ref items[at];

	//Puts one in and names it.
	internal ulong Add( in T item )
	{
		var handle = next++;

		if ( handle >= (ulong)items.Length ) Grow( handle );

		items[(int)handle] = item;
		live[(int)handle] = true;

		return handle;
	}

	//What a handle names, or false where it names nothing.
	internal bool TryGet( ulong handle, out T item )
	{
		if ( handle > 0 && handle < (ulong)live.Length && live[(int)handle] )
		{
			item = items[(int)handle];
			return true;
		}

		item = default;
		return false;
	}

	//The same, by reference and without the copy - null where the handle names nothing.
	internal ref T Find( ulong handle, out bool found )
	{
		if ( handle > 0 && handle < (ulong)live.Length && live[(int)handle] )
		{
			found = true;
			return ref items[(int)handle];
		}

		found = false;
		return ref System.Runtime.CompilerServices.Unsafe.NullRef<T>();
	}

	//Writes one back over the slot a handle names. False where it names nothing.
	internal bool Set( ulong handle, in T item )
	{
		if ( handle == 0 || handle >= (ulong)live.Length || !live[(int)handle] ) return false;

		items[(int)handle] = item;
		return true;
	}

	//Takes one out. What it named comes back, so the caller can destroy it.
	internal bool Remove( ulong handle, out T item )
	{
		if ( !TryGet( handle, out item ) ) return false;

		items[(int)handle] = default;
		live[(int)handle] = false;

		return true;
	}

	//Empties it, and starts naming from one again - for a device going away.
	internal void Clear()
	{
		System.Array.Clear( items );
		System.Array.Clear( live );
		next = 1;
	}

	private void Grow( ulong want )
	{
		var size = items.Length;

		while ( (ulong)size <= want ) size *= 2;

		System.Array.Resize( ref items, size );
		System.Array.Resize( ref live, size );
	}
}
