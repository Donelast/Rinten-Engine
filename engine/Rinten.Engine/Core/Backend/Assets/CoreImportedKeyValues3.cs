using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Rinten;

//Reader for binary KeyValues3: per-width value runs and a type stream, then LZ4
internal sealed partial class CoreImportedKeyValues3
{
	private static readonly Logger log = new( "Core" );

	//One of: null, a bool, a long, a ulong, a double, a string, bytes, a list of nodes, or names to nodes.
	private readonly object value;

	private CoreImportedKeyValues3( object value ) => this.value = value;

	//'KV3' and a version, as the first four bytes read little end first.
	private const uint Magic = 0x4B563300;

	//The version before they put the version in the magic.
	private const uint LegacyMagic = 0x03564B56;

	//What the buffer ends with, as a check that it was read right.
	private const uint Trailer = 0xFFEEDD00;

	/// <summary>Reads a block, or returns null where it is not key values at all or is written in a way this does not read.</summary>
	public static CoreImportedKeyValues3 Read( ReadOnlySpan<byte> block )
	{
		if ( block.Length < 24 ) return null;

		var magic = BinaryPrimitives.ReadUInt32LittleEndian( block );

		if ( magic == LegacyMagic )
		{
			log.Warning( "key values written before the version was in the header are not read" );
			return null;
		}

		if ( ( magic & 0xFFFFFF00 ) != Magic ) return null;

		var version = (int)( magic & 0xFF );

		if ( version is < 1 or > 4 )
		{
			log.Warning( $"key values version {version} is not one this reads" );
			return null;
		}

		try
		{
			return Parse( block, version );
		}
		catch ( System.Exception e ) when ( e is System.IndexOutOfRangeException or System.ArgumentException or System.IO.InvalidDataException )
		{
			log.Warning( $"key values ended before the tree in them did ({e.Message})" );
			return null;
		}
	}

	//The header, then the one buffer everything else is packed into.
	private static CoreImportedKeyValues3 Parse( ReadOnlySpan<byte> block, int version )
	{
		//Magic, then which of their formats this is - which says what the names mean, not how they are written, so it is read past.
		var at = 4 + 16;

		var compression = BinaryPrimitives.ReadUInt32LittleEndian( block[at..] );
		at += 4;

		var countBytes1 = 0;
		var countBytes4 = 0;
		var countBytes8 = 0;
		var countTypes = 0;
		var uncompressed = 0;
		var compressed = 0;
		var countBlocks = 0;
		var countBytes2 = 0;
		var blobBytes = 0;

		if ( version == 1 )
		{
			countBytes1 = Int( block, ref at );
			countBytes4 = Int( block, ref at );
			countBytes8 = Int( block, ref at );
			uncompressed = Int( block, ref at );

			compressed = block.Length - at;
		}
		else
		{
			//A dictionary to decompress against, and how big a frame is - neither of which anything
			//they ship uses: the blobs are compressed one by one rather than in frames of a size.
			var dictionary = BinaryPrimitives.ReadUInt16LittleEndian( block[at..] );
			at += 4;

			if ( dictionary != 0 )
			{
				log.Warning( "key values compressed against a dictionary are not read" );
				return null;
			}

			countBytes1 = Int( block, ref at );
			countBytes4 = Int( block, ref at );
			countBytes8 = Int( block, ref at );
			countTypes = Int( block, ref at );

			at += 4; //how many objects and how many arrays, which we count as we go

			uncompressed = Int( block, ref at );
			compressed = Int( block, ref at );
			countBlocks = Int( block, ref at );
			blobBytes = Int( block, ref at );
		}

		if ( version >= 4 )
		{
			countBytes2 = Int( block, ref at );

			//How many frames the blobs were cut into, written out where the newest of their versions does not size them all the same.
			var frames = Int( block, ref at );

			if ( frames > 0 )
			{
				log.Warning( "key values whose blob frames are sized one by one are not read" );
				return null;
			}
		}

		if ( uncompressed <= 0 || compressed <= 0 || at + compressed > block.Length ) return null;

		var buffer = new byte[uncompressed];

		switch ( compression )
		{
			case 0:
				if ( compressed != uncompressed ) return null;

				block.Slice( at, uncompressed ).CopyTo( buffer );
				break;

			case 1:
				if ( Compression.LZ4.DecompressBlock( block.Slice( at, compressed ), buffer ) != uncompressed )
				{
					log.Warning( "key values did not decompress to the size they claim" );
					return null;
				}

				break;

			default:
				log.Warning( $"key values compressed by method {compression} are not read" );
				return null;
		}

		return new Reader( buffer, version, countBytes1, countBytes2, countBytes4, countBytes8, countTypes,
			countBlocks, blobBytes, compression, block[( at + compressed )..] ).ReadTree();
	}

	private static int Int( ReadOnlySpan<byte> from, ref int at )
	{
		var value = BinaryPrimitives.ReadInt32LittleEndian( from[at..] );
		at += 4;

		return value;
	}

	//One cursor per run of values, walked in step with the types.
	private ref struct Reader
	{
		private readonly ReadOnlySpan<byte> buffer;
		private readonly int version;

		private int bytes1;
		private int bytes2;
		private int bytes4;
		private int bytes8;
		private int types;

		private readonly string[] strings;

		//Every blob end to end, and how long each of them is.
		private readonly byte[] blobs;
		private readonly int[] blobLengths;

		//Which blob the next one asked for is, and where it starts.
		private int blob;
		private int blobAt;

		public Reader( ReadOnlySpan<byte> buffer, int version,
			int countBytes1, int countBytes2, int countBytes4, int countBytes8, int countTypes,
			int countBlocks, int blobBytes, uint compression, ReadOnlySpan<byte> packed )
		{
			blobs = System.Array.Empty<byte>();

			this.buffer = buffer;
			this.version = version;

			//The runs sit one after another, each aligned to its own width.
			var at = 0;

			bytes1 = at;
			at += countBytes1;

			if ( countBytes2 > 0 )
			{
				at = Align( at, 2 );
				bytes2 = at;
				at += countBytes2 * 2;
			}

			//There is always at least one four byte value, because the string table's own count is written as one.
			if ( countBytes4 <= 0 ) throw new System.ArgumentException( "there is no string table" );

			at = Align( at, 4 );
			bytes4 = at;
			at += countBytes4 * 4;

			at = Align( at, 8 );

			if ( countBytes8 > 0 )
			{
				bytes8 = at;
				at += countBytes8 * 8;
			}

			//The string table's own count is the first four byte value.
			var count = BinaryPrimitives.ReadInt32LittleEndian( buffer[bytes4..] );
			bytes4 += 4;

			strings = new string[System.Math.Max( count, 0 )];

			var start = at;

			for ( var i = 0; i < strings.Length; i++ )
			{
				var end = buffer[at..].IndexOf( (byte)0 );
				if ( end < 0 ) throw new System.ArgumentException( "a string in the table does not end" );

				strings[i] = Encoding.UTF8.GetString( buffer.Slice( at, end ) );
				at += end + 1;
			}

			//The types run fills what is left before the trailer, which the header says the length of two different ways.
			types = at;

			var length = version == 1
				? buffer.Length - at - 4
				: countTypes - at + start;

			if ( length < 0 || at + length + 4 > buffer.Length )
				throw new System.ArgumentException( "the types do not fit in the buffer" );

			//How long each blob is comes after the types, before the trailer - the blobs themselves are elsewhere and have nothing in them saying where one ends.
			var after = at + length;

			blobLengths = new int[System.Math.Max( countBlocks, 0 )];

			for ( var i = 0; i < blobLengths.Length; i++ )
			{
				blobLengths[i] = BinaryPrimitives.ReadInt32LittleEndian( buffer[( after + i * 4 )..] );
				if ( blobLengths[i] < 0 ) throw new System.ArgumentException( "a blob is of no length" );
			}

			after += blobLengths.Length * 4;

			if ( after + 4 > buffer.Length )
				throw new System.ArgumentException( "the blob lengths do not fit in the buffer" );

			if ( BinaryPrimitives.ReadUInt32LittleEndian( buffer[after..] ) != Trailer )
				throw new System.ArgumentException( "the buffer does not end where it should" );

			if ( blobLengths.Length > 0 ) blobs = Unpack( packed, compression, blobBytes, blobLengths );
		}

		//Decodes the per-piece compressed blob section; each piece is decoded to find where the next starts
		private static byte[] Unpack( ReadOnlySpan<byte> packed, uint compression, int total, int[] lengths )
		{
			if ( total < 0 ) throw new System.ArgumentException( "the blobs are of no length" );

			var blobs = new byte[total];

			if ( compression == 0 )
			{
				if ( packed.Length < total ) throw new System.ArgumentException( "the blobs do not fit in the file" );

				packed[..total].CopyTo( blobs );
				return blobs;
			}

			var written = 0;
			var read = 0;

			foreach ( var length in lengths )
			{
				if ( written + length > total ) throw new System.ArgumentException( "the blobs run past the end of them" );

				read += Compression.LZ4.DecompressUnsized( packed[read..], blobs, written, length );
				written += length;
			}

			return blobs;
		}

		private static int Align( int at, int to ) => ( at + to - 1 ) & ~( to - 1 );

		/// <summary>The whole document, from its root value down.</summary>
		public CoreImportedKeyValues3 ReadTree() => ReadValue( ReadType() );

		//What the next value is. A high bit means a flag byte follows saying what the value
		//means - a resource name, a sound event - which changes nothing about how it is stored.
		private int ReadType()
		{
			var type = buffer[types++];

			if ( ( type & 0x80 ) == 0 ) return type;

			type &= (byte)( version >= 3 ? 0x3F : 0x7F );
			types++;

			return type;
		}

		private CoreImportedKeyValues3 ReadValue( int type )
		{
			switch ( type )
			{
				case 1: return new CoreImportedKeyValues3( null );
				case 13: return new CoreImportedKeyValues3( true );
				case 14: return new CoreImportedKeyValues3( false );
				case 15: return new CoreImportedKeyValues3( 0L );
				case 16: return new CoreImportedKeyValues3( 1L );
				case 17: return new CoreImportedKeyValues3( 0.0 );
				case 18: return new CoreImportedKeyValues3( 1.0 );

				case 2: return new CoreImportedKeyValues3( buffer[bytes1++] == 1 );
				case 23: return new CoreImportedKeyValues3( (long)buffer[bytes1++] );

				case 20: return new CoreImportedKeyValues3( (long)Read2() );
				case 21: return new CoreImportedKeyValues3( (long)(ushort)Read2() );

				case 11: return new CoreImportedKeyValues3( (long)Read4() );
				case 12: return new CoreImportedKeyValues3( (long)(uint)Read4() );
				case 19: return new CoreImportedKeyValues3( (double)BitConverter.Int32BitsToSingle( Read4() ) );

				case 3: return new CoreImportedKeyValues3( Read8() );
				case 4: return new CoreImportedKeyValues3( (ulong)Read8() );
				case 5: return new CoreImportedKeyValues3( BitConverter.Int64BitsToDouble( Read8() ) );

				case 6:
				{
					var id = Read4();
					return new CoreImportedKeyValues3( id < 0 || id >= strings.Length ? "" : strings[id] );
				}

				//Bytes, taken in the order they are asked for: the tree says where a blob is used and the
				//blobs are laid out in that order, so there is nothing in either end saying which is which.
				case 7:
				{
					if ( blob >= blobLengths.Length )
						throw new System.ArgumentException( "there are more blobs in the tree than in the buffer" );

					var length = blobLengths[blob++];

					if ( blobAt + length > blobs.Length )
						throw new System.ArgumentException( "a blob runs past the end of them" );

					var bytes = new byte[length];
					blobs.AsSpan( blobAt, length ).CopyTo( bytes );
					blobAt += length;

					return new CoreImportedKeyValues3( bytes );
				}

				//An array of whatever each element says it is.
				case 8:
				{
					var count = Read4();
					var items = new List<CoreImportedKeyValues3>( System.Math.Max( count, 0 ) );

					for ( var i = 0; i < count; i++ )
					{
						items.Add( ReadValue( ReadType() ) );
					}

					return new CoreImportedKeyValues3( items );
				}

				//An array where every element is the same type, so the type is written once - which is most of them, and most of the saving.
				case 10:
				case 24:
				{
					var count = type == 24 ? buffer[bytes1++] : Read4();
					var element = ReadType();
					var items = new List<CoreImportedKeyValues3>( System.Math.Max( count, 0 ) );

					for ( var i = 0; i < count; i++ )
					{
						items.Add( ReadValue( element ) );
					}

					return new CoreImportedKeyValues3( items );
				}

				case 9:
				{
					var count = Read4();
					var fields = new Dictionary<string, CoreImportedKeyValues3>( System.Math.Max( count, 0 ) );

					for ( var i = 0; i < count; i++ )
					{
						var valueType = ReadType();
						var id = Read4();
						var name = id < 0 || id >= strings.Length ? "" : strings[id];

						fields[name] = ReadValue( valueType );
					}

					return new CoreImportedKeyValues3( fields );
				}

				default:
					throw new System.ArgumentException( $"value type {type} is not one this reads" );
			}
		}

		private short Read2()
		{
			var value = BinaryPrimitives.ReadInt16LittleEndian( buffer[bytes2..] );
			bytes2 += 2;

			return value;
		}

		private int Read4()
		{
			var value = BinaryPrimitives.ReadInt32LittleEndian( buffer[bytes4..] );
			bytes4 += 4;

			return value;
		}

		private long Read8()
		{
			var value = BinaryPrimitives.ReadInt64LittleEndian( buffer[bytes8..] );
			bytes8 += 8;

			return value;
		}
	}

	//-- what a caller does with the tree ---------------------------------------

	public bool IsObject => value is Dictionary<string, CoreImportedKeyValues3>;

	public bool IsArray => value is List<CoreImportedKeyValues3>;

	/// <summary>How many things are in this, where it holds any.</summary>
	public int Count => value switch
	{
		List<CoreImportedKeyValues3> list => list.Count,
		Dictionary<string, CoreImportedKeyValues3> fields => fields.Count,
		_ => 0,
	};

	/// <summary>A field by name, or null where there is none.</summary>
	public CoreImportedKeyValues3 this[string key]
		=> value is Dictionary<string, CoreImportedKeyValues3> fields && fields.TryGetValue( key, out var found ) ? found : null;

	/// <summary>An element, or null where the index is past the end.</summary>
	public CoreImportedKeyValues3 this[int index]
		=> value is List<CoreImportedKeyValues3> list && index >= 0 && index < list.Count ? list[index] : null;

	/// <summary>The elements, or nothing where this is not an array.</summary>
	public IReadOnlyList<CoreImportedKeyValues3> Items
		=> value as List<CoreImportedKeyValues3> ?? (IReadOnlyList<CoreImportedKeyValues3>)System.Array.Empty<CoreImportedKeyValues3>();

	/// <summary>The names in this, where it is an object.</summary>
	public IEnumerable<string> Keys
		=> value is Dictionary<string, CoreImportedKeyValues3> fields ? fields.Keys : System.Array.Empty<string>();

	public string AsString( string fallback = null ) => value as string ?? fallback;

	public double AsNumber( double fallback = 0.0 ) => value switch
	{
		long number => number,
		ulong number => number,
		double number => number,
		bool flag => flag ? 1.0 : 0.0,
		_ => fallback,
	};

	public int AsInt( int fallback = 0 ) => value switch
	{
		long number => (int)number,
		ulong number => (int)number,
		double number => (int)number,
		bool flag => flag ? 1 : 0,
		_ => fallback,
	};

	public float AsFloat( float fallback = 0.0f ) => (float)AsNumber( fallback );

	public bool AsBool( bool fallback = false ) => value is bool flag ? flag : fallback;

	/// <summary>The bytes, where this is a blob - a compressed animation track, a mesh's vertices. Null for anything else.</summary>
	public byte[] AsBytes() => value as byte[];

	/// <summary>The bytes of a named blob, or null.</summary>
	public byte[] Bytes( string key ) => this[key]?.AsBytes();

	//-- the same, reaching through a name --------------------------------------

	public string String( string key, string fallback = null ) => this[key]?.AsString( fallback ) ?? fallback;

	public int Int( string key, int fallback = 0 ) => this[key]?.AsInt( fallback ) ?? fallback;

	public float Float( string key, float fallback = 0.0f ) => this[key]?.AsFloat( fallback ) ?? fallback;

	public bool Bool( string key, bool fallback = false ) => this[key]?.AsBool( fallback ) ?? fallback;

	/// <summary>An array by name, empty where there is none.</summary>
	public IReadOnlyList<CoreImportedKeyValues3> Array( string key )
		=> this[key]?.Items ?? System.Array.Empty<CoreImportedKeyValues3>();
}
