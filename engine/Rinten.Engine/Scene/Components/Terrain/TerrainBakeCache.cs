using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Rinten.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rinten;

//On-disk cache of baked chunk models, keyed by everything the bake reads, so it never goes stale
//Chunks under modifiers, flat brush bakes and headless bakes are not cached
internal static class TerrainBakeCache
{
	private static readonly Logger log = new( "Terrain" );

	//Bumped whenever what a bake comes to changes - the model format, the cluster builder, what a
	//vertex carries. Everything written before is then a different key and is swept up by the budget.
	private const uint Version = 2;   //2: eight layer weights a vertex

	//The file's own magic, so half a file or somebody else's is a miss rather than a crash.
	private const uint Magic = 0x4B425452; //"RTBK"

	//Past this the oldest go. A default terrain is some tens of megabytes.
	private const long Budget = 1024L * 1024 * 1024;

	private static readonly bool off = Environment.GetEnvironmentVariable( "RINTEN_NO_TERRAIN_CACHE" ) == "1";

	//Beside the shader cache, for the same reason: it belongs to the machine, not to the project.
	private static readonly string folder = Where();

	private static string Where()
	{
		try
		{
			var root = Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData );
			if ( string.IsNullOrEmpty( root ) ) root = Path.GetTempPath();

			return Path.Combine( root, "Rinten", "terrain" );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	/// <summary>What this job would be filed under, or null where it is not to be cached at all.</summary>
	public static string Key( TerrainChunkJob job )
	{
		if ( off || folder is null || job is null ) return null;
		if ( !job.Draws || job.Coarse || job.Modifiers.Length > 0 ) return null;
		if ( job.Ground is not { } soup || soup.Indices.Count < 3 ) return null;

		//Two hashes of the same bytes under different seeds - a hundred and twenty-eight
		//bits, because a collision here is not a slow frame but the wrong ground.
		var one = new XxHash3();
		var two = new XxHash3( unchecked((long)0x5bd1e9955bd1e995) );

		void Feed( ReadOnlySpan<byte> bytes )
		{
			one.Append( bytes );
			two.Append( bytes );
		}

		Span<byte> head = stackalloc byte[28];

		BinaryPrimitives.WriteUInt32LittleEndian( head[..4], Version );
		BinaryPrimitives.WriteSingleLittleEndian( head[4..8], job.Min.x );
		BinaryPrimitives.WriteSingleLittleEndian( head[8..12], job.Min.y );
		BinaryPrimitives.WriteSingleLittleEndian( head[12..16], job.Max.x );
		BinaryPrimitives.WriteSingleLittleEndian( head[16..20], job.Max.y );
		BinaryPrimitives.WriteUInt32LittleEndian( head[20..24], (uint)Unsafe.SizeOf<TerrainSoup.Vertex>() );

		//And the model builder's generation: a chunk is a model, and one built by yesterday's builder was found under today's key and drawn for ever.
		BinaryPrimitives.WriteUInt32LittleEndian( head[24..28], ModelFormat.ImportGeneration );

		Feed( head );

		Feed( MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( soup.Vertices ) ) );
		Feed( MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( soup.Indices ) ) );
		Feed( MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( soup.Materials ) ) );

		//What a material number in this chunk means. Only the name matters: that is all the model file keeps of a material.
		foreach ( var material in job.Materials )
			Feed( System.Text.Encoding.UTF8.GetBytes( ( material?.ResourcePath ?? material?.Name ?? "" ) + "\n" ) );

		Span<byte> key = stackalloc byte[16];

		BinaryPrimitives.WriteUInt64LittleEndian( key[..8], one.GetCurrentHashAsUInt64() );
		BinaryPrimitives.WriteUInt64LittleEndian( key[8..], two.GetCurrentHashAsUInt64() );

		return Convert.ToHexString( key );
	}

	private static string PathOf( string key ) => Path.Combine( folder, key + ".bake" );

	/// <summary>How many chunks this run read back and how many it had to bake - what the log says after a layout.</summary>
	public static int Hits => hits;
	public static int Misses => misses;

	private static int hits, misses;

	/// <summary>What was filed under that key, or null.</summary>
	public static TerrainChunkBake Read( string key )
	{
		if ( key is null ) return null;

		byte[] bytes;

		try
		{
			bytes = File.ReadAllBytes( PathOf( key ) );
		}
		catch ( Exception )
		{
			//Not there, or somebody is writing it this moment. Either way it is a miss, and a miss costs a bake.
			System.Threading.Interlocked.Increment( ref misses );
			return null;
		}

		try
		{
			var read = new Reader( bytes );

			if ( read.U32() != Magic || read.U32() != Version ) return null;
			if ( read.U32() != (uint)Unsafe.SizeOf<TerrainSoup.Vertex>() ) return null;

			var bake = new TerrainChunkBake { Ground = new TerrainSoup() };

			bake.GroundBytes = read.Blob();
			bake.SetInBytes = read.Blob();

			read.Into( bake.CollisionPoints );
			read.Into( bake.CollisionIndices );

			read.Into( bake.Ground.Vertices );
			read.Into( bake.Ground.Indices );
			read.Into( bake.Ground.Materials );

			if ( !read.Whole )
			{
				System.Threading.Interlocked.Increment( ref misses );
				return null;
			}

			System.Threading.Interlocked.Increment( ref hits );
			return bake;
		}
		catch ( Exception )
		{
			System.Threading.Interlocked.Increment( ref misses );
			return null;
		}
	}

	/// <summary>Files a finished bake, having already let go of the geometry it was written from.</summary>
	public static void Write( string key, TerrainChunkBake bake )
	{
		if ( key is null || bake?.Ground is null ) return;

		try
		{
			Directory.CreateDirectory( folder );

			var write = new Writer();

			write.U32( Magic );
			write.U32( Version );
			write.U32( (uint)Unsafe.SizeOf<TerrainSoup.Vertex>() );

			write.Blob( bake.GroundBytes );
			write.Blob( bake.SetInBytes );

			write.From( bake.CollisionPoints );
			write.From( bake.CollisionIndices );

			write.From( bake.Ground.Vertices );
			write.From( bake.Ground.Indices );
			write.From( bake.Ground.Materials );

			//Named for whoever is writing it, so two of them baking the same chunk do not write over
			//each other half way, and moved into place in one go so a reader sees a whole file or none.
			var temporary = $"{PathOf( key )}.{Environment.ProcessId}.{Environment.CurrentManagedThreadId}.part";

			File.WriteAllBytes( temporary, write.Bytes() );
			File.Move( temporary, PathOf( key ), true );
		}
		catch ( Exception e )
		{
			//A cache that cannot be written is a cache that is not used. Said once, because the next chunk will fail the same way.
			if ( System.Threading.Interlocked.Exchange( ref complained, 1 ) == 0 )
				log.Info( $"the terrain bake cache is not being written - {e.Message}" );
		}

		Sweep();
	}

	private static int complained;

	//-- keeping it to a size -------------------------------------------------

	private static int swept;

	//Once a run, in the background: the oldest go until what is left is well under the budget. By when a file was last written, which a read does
	//not change - so this drops what nothing has baked in a long time, which after a sculpt is every key the old shape of the ground was under.
	private static void Sweep()
	{
		if ( System.Threading.Interlocked.Exchange( ref swept, 1 ) != 0 ) return;

		System.Threading.Tasks.Task.Run( () =>
		{
			try
			{
				var files = new DirectoryInfo( folder ).GetFiles( "*.bake" );

				long held = 0;
				foreach ( var file in files ) held += file.Length;

				if ( held <= Budget ) return;

				System.Array.Sort( files, ( x, y ) => x.LastWriteTimeUtc.CompareTo( y.LastWriteTimeUtc ) );

				foreach ( var file in files )
				{
					if ( held <= ( Budget * 4 ) / 5 ) break;

					held -= file.Length;

					try { file.Delete(); }
					catch ( Exception ) { }
				}
			}
			catch ( Exception )
			{
			}
		} );
	}

	//-- the bytes ------------------------------------------------------------

	private sealed class Writer
	{
		private readonly List<byte> bytes = new();

		public void U32( uint value )
		{
			Span<byte> four = stackalloc byte[4];
			BinaryPrimitives.WriteUInt32LittleEndian( four, value );
			bytes.AddRange( four );
		}

		/// <summary>A run of bytes, or nothing at all - which reads back as null.</summary>
		public void Blob( byte[] blob )
		{
			U32( blob is null ? 0u : (uint)blob.Length );
			if ( blob is not null ) bytes.AddRange( blob );
		}

		/// <summary>A list of something flat, as the bytes it is made of.</summary>
		public void From<T>( List<T> list ) where T : unmanaged
		{
			U32( (uint)list.Count );
			bytes.AddRange( MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( list ) ) );
		}

		public byte[] Bytes() => bytes.ToArray();
	}

	private ref struct Reader
	{
		private readonly ReadOnlySpan<byte> bytes;
		private int at;

		public Reader( ReadOnlySpan<byte> bytes )
		{
			this.bytes = bytes;
			at = 0;
		}

		/// <summary>Whether the file ended exactly where the reading did.</summary>
		public readonly bool Whole => at == bytes.Length;

		public uint U32()
		{
			var value = BinaryPrimitives.ReadUInt32LittleEndian( bytes.Slice( at, 4 ) );
			at += 4;

			return value;
		}

		public byte[] Blob()
		{
			var count = (int)U32();
			if ( count == 0 ) return null;

			var blob = bytes.Slice( at, count ).ToArray();
			at += count;

			return blob;
		}

		public void Into<T>( List<T> list ) where T : unmanaged
		{
			var count = (int)U32();
			var size = count * Unsafe.SizeOf<T>();

			list.Clear();
			list.AddRange( MemoryMarshal.Cast<byte, T>( bytes.Slice( at, size ) ) );

			at += size;
		}
	}
}
