using System;
using System.Buffers.Binary;
using System.IO;

namespace Rinten;

//KTX2 UASTC texture for web builds; only the header is read, the browser transcodes the blocks
internal sealed class CoreUniversalTexture
{
	private static ReadOnlySpan<byte> Identifier => [0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A];

	//KHR_DF_TRANSFER_SRGB.
	private const int TransferSrgb = 2;

	//KHR_DF_CHANNEL_UASTC_RGBA and _RRRG: the two of UASTC's channel layouts with an alpha in them.
	private const int ChannelRgba = 3;
	private const int ChannelRrrg = 5;

	public byte[] Bytes { get; }
	public int Width { get; }
	public int Height { get; }
	public int Levels { get; }
	public bool IsSrgb { get; }
	public bool HasAlpha { get; }

	private CoreUniversalTexture( byte[] bytes, int width, int height, int levels, bool srgb, bool alpha )
	{
		Bytes = bytes;
		Width = width;
		Height = height;
		Levels = levels;
		IsSrgb = srgb;
		HasAlpha = alpha;
	}

	/// <summary>Whether a path names one of these, by the name it is carried under - see CompiledPaths.Universal.</summary>
	public static bool IsOne( string path ) => path is not null && path.EndsWith( ".ktx2", StringComparison.OrdinalIgnoreCase );

	/// <summary>Reads one, or null where the file is missing or not a two-dimensional KTX2 of Basis blocks.</summary>
	public static CoreUniversalTexture Load( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;

		byte[] bytes;

		try
		{
			if ( !File.Exists( path ) ) return null;

			bytes = SealedFile.ReadAllBytes( path );
		}
		catch ( IOException )
		{
			return null;
		}
		catch ( UnauthorizedAccessException )
		{
			return null;
		}

		return FromBytes( bytes );
	}

	public static CoreUniversalTexture FromBytes( byte[] bytes )
	{
		if ( bytes is null || bytes.Length < 80 || !bytes.AsSpan( 0, 12 ).SequenceEqual( Identifier ) ) return null;

		uint U32( int at ) => BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( at ) );

		//vkFormat nought is "not a Vulkan format": Basis blocks, which the transcoder turns into one.
		if ( U32( 12 ) != 0 ) return null;

		var width = (int)U32( 20 );
		var height = (int)U32( 24 );
		var depth = U32( 28 );
		var layers = U32( 32 );
		var faces = U32( 36 );
		var levels = (int)Math.Max( 1u, U32( 40 ) );

		if ( width <= 0 || height <= 0 || depth > 1 || layers > 1 || faces != 1 ) return null;

		//The data format descriptor's basic block: its transfer function, and its first sample's channel - which, for UASTC, says which layout it is.
		var dfd = (int)U32( 48 );
		var dfdLength = (int)U32( 52 );
		var srgb = false;
		var alpha = true;

		if ( dfd > 0 && dfdLength >= 4 + 24 + 16 && dfd + dfdLength <= bytes.Length )
		{
			var block = dfd + 4;
			srgb = bytes[block + 10] == TransferSrgb;

			var channel = bytes[block + 24 + 3] & 0x0F;
			alpha = channel == ChannelRgba || channel == ChannelRrrg;
		}

		return new CoreUniversalTexture( bytes, width, height, levels, srgb, alpha );
	}

	/// <summary>A copy starting at mip level top, for texture streaming; only the header and level index change</summary>
	public CoreUniversalTexture From( int top )
	{
		if ( top <= 0 ) return this;
		if ( top >= Levels ) top = Levels - 1;

		var bytes = Bytes;

		ulong U64( int at ) => BinaryPrimitives.ReadUInt64LittleEndian( bytes.AsSpan( at ) );

		const int Header = 80;
		const int Entry = 24;
		var indexEnd = Header + ( Levels * Entry );
		if ( bytes.Length < indexEnd ) return null;

		//Where the first level's bytes begin: everything before it, after the index, is the descriptor, the key/values and the supercompression data.
		var dataStart = long.MaxValue;

		for ( var k = 0; k < Levels; k++ )
		{
			var at = (long)U64( Header + ( k * Entry ) );
			var length = (long)U64( Header + ( k * Entry ) + 8 );
			if ( at < indexEnd || at + length > bytes.Length ) return null;
			dataStart = Math.Min( dataStart, at );
		}

		var kept = Levels - top;
		var shift = top * Entry;
		var middle = (int)( dataStart - indexEnd );
		var size = Header + ( kept * Entry ) + middle;

		var levelsAt = new long[kept];

		for ( var k = 0; k < kept; k++ )
		{
			size = ( size + 15 ) & ~15;
			levelsAt[k] = size;
			size += (int)U64( Header + ( ( top + k ) * Entry ) + 8 );
		}

		var made = new byte[size];
		bytes.AsSpan( 0, Header ).CopyTo( made );
		bytes.AsSpan( indexEnd, middle ).CopyTo( made.AsSpan( Header + ( kept * Entry ) ) );

		var into = made.AsSpan();
		BinaryPrimitives.WriteUInt32LittleEndian( into[20..], (uint)Math.Max( 1, Width >> top ) );
		BinaryPrimitives.WriteUInt32LittleEndian( into[24..], (uint)Math.Max( 1, Height >> top ) );
		BinaryPrimitives.WriteUInt32LittleEndian( into[40..], (uint)kept );

		//The descriptor's, the key/values' and the supercompression data's places, moved up with them - nought stays nought.
		void Moved32( int at )
		{
			var was = BinaryPrimitives.ReadUInt32LittleEndian( made.AsSpan( at ) );
			if ( was != 0 ) BinaryPrimitives.WriteUInt32LittleEndian( made.AsSpan( at ), (uint)( was - shift ) );
		}

		Moved32( 48 );
		Moved32( 56 );

		var sgd = BinaryPrimitives.ReadUInt64LittleEndian( into[64..] );
		if ( sgd != 0 ) BinaryPrimitives.WriteUInt64LittleEndian( into[64..], sgd - (ulong)shift );

		for ( var k = 0; k < kept; k++ )
		{
			var from = Header + ( ( top + k ) * Entry );
			var to = Header + ( k * Entry );
			var at = (int)U64( from );
			var length = (int)U64( from + 8 );

			BinaryPrimitives.WriteUInt64LittleEndian( into[to..], (ulong)levelsAt[k] );
			BinaryPrimitives.WriteUInt64LittleEndian( into[( to + 8 )..], (ulong)length );
			BinaryPrimitives.WriteUInt64LittleEndian( into[( to + 16 )..], U64( from + 16 ) );

			bytes.AsSpan( at, length ).CopyTo( into[(int)levelsAt[k]..] );
		}

		return new CoreUniversalTexture( made, Math.Max( 1, Width >> top ), Math.Max( 1, Height >> top ), kept, IsSrgb, HasAlpha );
	}
}
