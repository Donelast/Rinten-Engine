using System;
using System.Collections.Generic;

namespace Rinten;

//Buffers on the device, WebGPU. One kind, filled at creation, usable as vertex, index and storage. The handles are
//the ABI's: they count from one and are never reused while a buffer lives, so a pair of them is a pair of buffers.
internal static unsafe class BrowserBuffers
{
	private struct Buffer
	{
		public int Handle;
		public long Bytes;
	}

	//A bind group naming two buffers, and which two - so that destroying either can find it again.
	private struct ObjectSet
	{
		public int Set;
		public ulong Geometry;
		public ulong Data;
	}

	private static readonly Dictionary<ulong, Buffer> buffers = new();
	private static readonly Dictionary<ulong, ObjectSet> objectSets = new();
	private static ulong nextHandle = 1;

	private static ulong ObjectKey( ulong geometry, ulong data ) => ( geometry * 1099511628211ul ) ^ data;

	//The page's handle for a buffer, or zero for no such buffer.
	internal static int Get( ulong handle )
		=> buffers.TryGetValue( handle, out var found ) ? found.Handle : 0;

	//How many bytes were asked for when it was made, or zero for no such buffer.
	internal static long BytesOf( ulong handle )
		=> buffers.TryGetValue( handle, out var found ) ? found.Bytes : 0;

	//Cached bind group for an object's vertex (0) and extra data (1) buffers; geometry fills in for 1
	internal static int ObjectSetGet( ulong geometry, ulong data )
	{
		if ( geometry == 0 ) return 0;
		if ( data == 0 ) data = geometry;

		var key = ObjectKey( geometry, data );
		if ( objectSets.TryGetValue( key, out var found ) ) return found.Set;

		var geometryBuffer = Get( geometry );
		var dataBuffer = Get( data );
		if ( geometryBuffer == 0 || dataBuffer == 0 ) return 0;

		var set = BrowserDevice.MakeBindGroup( BrowserDevice.objectSetLayout,
			BrowserDevice.Bind.Buffer( 0, geometryBuffer, BytesOf( geometry ) ),
			BrowserDevice.Bind.Buffer( 1, dataBuffer, BytesOf( data ) ) );

		if ( set == 0 ) return 0;

		objectSets[key] = new ObjectSet { Set = set, Geometry = geometry, Data = data };
		return set;
	}

	internal static void DestroyObjectSets()
	{
		foreach ( var pair in objectSets )
		{
			if ( pair.Value.Set != 0 ) BrowserNative.Release( pair.Value.Set );
		}

		objectSets.Clear();
	}

	internal static void DestroyBuffers()
	{
		DestroyObjectSets();

		foreach ( var pair in buffers )
		{
			if ( pair.Value.Handle == 0 ) continue;

			BrowserDevice.trackedBufferBytes -= pair.Value.Bytes;
			BrowserDevice.trackedBufferCount -= 1;
			BrowserNative.Release( pair.Value.Handle );
		}

		buffers.Clear();
	}

	//A buffer with bytes in it, usable for every one of the things a draw can read out of it and for either end of a copy.
	internal static ulong Create( byte* data, long size )
	{
		if ( !DeviceThere() ) return 0;

		if ( data is null || size <= 0 )
		{
			BrowserDevice.infoText = "a buffer needs some bytes";
			return 0;
		}

		return Made( size, BrowserDevice.MakeBuffer( new Span<byte>( data, (int)size ), Usage ) );
	}

	//A buffer of a size, written later - see Write.
	internal static ulong Reserve( long size )
	{
		if ( !DeviceThere() ) return 0;

		if ( size <= 0 )
		{
			BrowserDevice.infoText = "a buffer needs a size";
			return 0;
		}

		return Made( size, BrowserDevice.MakeBuffer( size, Usage ) );
	}

	//Bytes into a buffer that is already there. Four-byte aligned at both ends, as WebGPU writes; anything else
	//is refused rather than rounded, since rounding the end up would write over the next thing in the buffer.
	internal static int Write( ulong buffer, long offset, byte* data, long size )
	{
		if ( !buffers.TryGetValue( buffer, out var found ) || data is null || size <= 0 ) return 0;
		if ( offset < 0 || offset + size > found.Bytes ) return 0;

		if ( ( offset & 3 ) != 0 || ( size & 3 ) != 0 )
		{
			BrowserDevice.infoText = "a buffer write on the web is whole words";
			return 0;
		}

		BrowserNative.WriteBuffer( found.Handle, offset, new Span<byte>( data, (int)size ) );
		return 1;
	}

	//Bytes from one buffer into another, in the device's order.
	internal static int Copy( ulong from, long fromOffset, ulong into, long intoOffset, long size )
	{
		if ( !buffers.TryGetValue( from, out var source ) || !buffers.TryGetValue( into, out var target ) ) return 0;
		if ( size <= 0 || fromOffset < 0 || intoOffset < 0 ) return 0;
		if ( fromOffset + size > source.Bytes || intoOffset + size > target.Bytes ) return 0;

		BrowserNative.CopyBuffer( source.Handle, fromOffset, target.Handle, intoOffset, size );
		return 1;
	}

	//Everything a draw can read a buffer as, and either end of a copy.
	private const int Usage = BrowserDevice.BufferUse.Vertex | BrowserDevice.BufferUse.Index |
		BrowserDevice.BufferUse.Storage | BrowserDevice.BufferUse.CopyDst | BrowserDevice.BufferUse.CopySrc;

	private static bool DeviceThere()
	{
		if ( BrowserDevice.Ready ) return true;

		BrowserDevice.infoText = "there is no device to put a buffer on";
		return false;
	}

	private static ulong Made( long size, int made )
	{
		if ( made == 0 )
		{
			BrowserDevice.infoText = "the buffer would not be made";
			return 0;
		}

		BrowserDevice.trackedBufferBytes += size;
		BrowserDevice.trackedBufferCount += 1;

		var handle = nextHandle++;
		buffers[handle] = new Buffer { Handle = made, Bytes = size };
		return handle;
	}

	//Lets a buffer go, and every bind group that named it - a group outlives what it points at otherwise,
	//and the next draw through it would be reading something that has been given back to the browser.
	internal static void Destroy( ulong buffer )
	{
		if ( !buffers.TryGetValue( buffer, out var found ) ) return;

		List<ulong> dropped = null;

		foreach ( var pair in objectSets )
		{
			if ( pair.Value.Geometry != buffer && pair.Value.Data != buffer ) continue;

			if ( pair.Value.Set != 0 ) BrowserNative.Release( pair.Value.Set );
			( dropped ??= new List<ulong>() ).Add( pair.Key );
		}

		if ( dropped is not null )
		{
			foreach ( var key in dropped ) objectSets.Remove( key );
		}

		if ( found.Handle != 0 )
		{
			BrowserDevice.trackedBufferBytes -= found.Bytes;
			BrowserDevice.trackedBufferCount -= 1;
			BrowserNative.Release( found.Handle );
		}

		buffers.Remove( buffer );
	}
}
