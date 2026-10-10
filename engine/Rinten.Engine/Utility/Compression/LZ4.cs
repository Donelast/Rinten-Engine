using System.IO;
using System.IO.Compression;
using Rinten.Native;

namespace Rinten.Compression;

/// <summary>Encode and decode LZ4 compressed data.</summary>
public static class LZ4
{
	private static int CompressionLevelToLZ4Level( CompressionLevel level )
	{
		return level switch
		{
			CompressionLevel.NoCompression => throw new ArgumentException( "NoCompression is not supported for LZ4." ),
			CompressionLevel.Fastest => 1, //Fast mode uses LZ4_compress_default
			CompressionLevel.Optimal => 2,
			CompressionLevel.SmallestSize => 12,
			_ => 1,
		};
	}

	//Worst-case compressed size for the given input length. Use to size a destination buffer.
	internal static int MaxCompressedSize( int sourceLength )
	{
		if ( sourceLength <= 0 ) return 0;

		return Lz4.LZ4_compressBound( sourceLength );
	}

	/// <summary>Encode data as an LZ4 block.</summary>
	public static byte[] CompressBlock( ReadOnlySpan<byte> data, CompressionLevel compressionLevel = CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		using var compressed = new PooledSpan<byte>( MaxCompressedSize( data.Length ) );
		int resultLength = CompressBlock( data, compressed.Span, compressionLevel );
		return compressed.Span.Slice( 0, resultLength ).ToArray();
	}

	//Encode an LZ4 block into a caller-provided buffer (must be at least MaxCompressedSize bytes). Returns bytes written.
	internal static int CompressBlock( ReadOnlySpan<byte> data, Span<byte> dest, CompressionLevel compressionLevel = CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return 0;

		int maxLength = MaxCompressedSize( data.Length );
		if ( dest.Length < maxLength )
			throw new ArgumentException( $"Destination buffer too small ({dest.Length}b, need at least {maxLength}b).", nameof( dest ) );

		int resultLength;
		unsafe
		{
			fixed ( byte* srcPtr = data )
			fixed ( byte* dstPtr = dest )
			{
				int level = CompressionLevelToLZ4Level( compressionLevel );
				if ( level <= 1 )
				{
					//Use fast compression
					resultLength = Lz4.LZ4_compress_default( srcPtr, dstPtr, data.Length, dest.Length );
				}
				else
				{
					//Use HC (high compression) mode
					resultLength = Lz4.LZ4_compress_HC( srcPtr, dstPtr, data.Length, dest.Length, level );
				}
			}
		}

		if ( resultLength <= 0 )
			throw new InvalidDataException( "LZ4 encode failed." );

		return resultLength;
	}


	/// <summary>Decode raw LZ4 block data.</summary>
	public static int DecompressBlock( ReadOnlySpan<byte> src, Span<byte> dest )
	{
		if ( src.IsEmpty || dest.IsEmpty )
			return 0;

		int resultLength;
		unsafe
		{
			fixed ( byte* srcPtr = src )
			fixed ( byte* dstPtr = dest )
			{
				//The safe one: it is told how much room it has and stops there, however the compressed bytes were arrived at.
				resultLength = Lz4.LZ4_decompress_safe( srcPtr, dstPtr, src.Length, dest.Length );
			}
		}

		if ( resultLength < 0 )
			throw new InvalidDataException( "LZ4 decode failed." );

		return resultLength;
	}

	//Decodes one chained block of known output length into dest at `at`; returns bytes of src consumed
	internal static int DecompressUnsized( ReadOnlySpan<byte> src, Span<byte> dest, int at, int length )
	{
		if ( at < 0 || length < 0 || at + length > dest.Length )
			throw new ArgumentException( "the block does not fit in what it decodes into." );

		var read = 0;
		var written = at;
		var end = at + length;

		while ( written < end )
		{
			if ( read >= src.Length ) throw new InvalidDataException( "LZ4 block ends before what it holds does." );

			var token = src[read++];

			//A run of bytes to take as they are. Fifteen means longer, said in bytes of two hundred and fifty five until one is not.
			var literals = token >> 4;

			if ( literals == 15 )
			{
				byte more;

				do
				{
					if ( read >= src.Length ) throw new InvalidDataException( "LZ4 block ends inside a length." );

					more = src[read++];
					literals += more;
				}
				while ( more == 255 );
			}

			if ( read + literals > src.Length || written + literals > end )
				throw new InvalidDataException( "LZ4 literals run past the end." );

			src.Slice( read, literals ).CopyTo( dest[written..] );
			read += literals;
			written += literals;

			//Every block ends on its literals, so this is where it does.
			if ( written >= end ) break;

			if ( read + 2 > src.Length ) throw new InvalidDataException( "LZ4 block ends before a match." );

			var offset = src[read] | ( src[read + 1] << 8 );
			read += 2;

			if ( offset <= 0 || offset > written ) throw new InvalidDataException( "LZ4 match reaches before the start." );

			var match = token & 0xF;

			if ( match == 15 )
			{
				byte more;

				do
				{
					if ( read >= src.Length ) throw new InvalidDataException( "LZ4 block ends inside a length." );

					more = src[read++];
					match += more;
				}
				while ( more == 255 );
			}

			match += 4;

			if ( written + match > end ) throw new InvalidDataException( "LZ4 match runs past the end." );

			//One byte at a time on purpose: a match is allowed to overlap what it is copying, which is how a run of the same byte is written.
			var from = written - offset;

			for ( var i = 0; i < match; i++ )
				dest[written++] = dest[from++];
		}

		return read;
	}

	/// <summary>Encode data as an LZ4 frame (standard LZ4 frame format, compatible with lz4 CLI).</summary>
	public static unsafe byte[] CompressFrame( ReadOnlySpan<byte> data, CompressionLevel compressionLevel = CompressionLevel.Fastest )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		int maxFrameSize = (int)Lz4.LZ4F_compressFrameBound( (nuint)data.Length, null );
		using var compressed = new PooledSpan<byte>( maxFrameSize );

		var preferences = default( Lz4.LZ4F_preferences_t );
		preferences.compressionLevel = CompressionLevelToLZ4Level( compressionLevel );

		//The size goes in the header. Whoever reads it back can then ask how big the
		//contents are before making room for them, which is what DecompressFrame does.
		preferences.frameInfo.contentSize = (ulong)data.Length;

		nuint written;

		fixed ( byte* srcPtr = data )
		fixed ( byte* dstPtr = compressed.Span )
		{
			written = Lz4.LZ4F_compressFrame( dstPtr, (nuint)maxFrameSize, srcPtr, (nuint)data.Length, &preferences );
		}

		if ( Lz4.LZ4F_isError( written ) != 0 || written == 0 )
			throw new InvalidDataException( "LZ4 frame encode failed." );

		return compressed.Span.Slice( 0, (int)written ).ToArray();
	}

	//How big the thing in a frame is, out of its header. -1 if it does not say.
	private static unsafe long FrameContentSize( ReadOnlySpan<byte> data )
	{
		if ( data.IsEmpty ) return -1;

		void* context = null;

		if ( Lz4.LZ4F_isError( Lz4.LZ4F_createDecompressionContext( &context, Lz4.LZ4F_VERSION ) ) != 0 ) return -1;

		var info = default( Lz4.LZ4F_frameInfo_t );
		var consumed = (nuint)data.Length;
		nuint result;

		fixed ( byte* srcPtr = data )
		{
			result = Lz4.LZ4F_getFrameInfo( context, &info, srcPtr, &consumed );
		}

		Lz4.LZ4F_freeDecompressionContext( context );

		if ( Lz4.LZ4F_isError( result ) != 0 ) return -1;

		//Zero means the frame did not say, which is not the same as an empty one.
		return info.contentSize == 0 ? -1 : (long)info.contentSize;
	}

	/// <summary>Decode an LZ4 frame (standard LZ4 frame format, compatible with lz4 CLI).</summary>
	public static unsafe byte[] DecompressFrame( ReadOnlySpan<byte> data )
	{
		if ( data.IsEmpty )
			return Array.Empty<byte>();

		if ( data.Length < 7 ) //Minimum LZ4 frame header size
			throw new InvalidDataException( "LZ4 frame too small." );

		//Get the content size from frame header (we always set it during compression)
		long contentSize = FrameContentSize( data );

		if ( contentSize < 0 )
			throw new InvalidDataException( "Failed to read LZ4 frame header." );

		if ( contentSize > 1024 * 1024 * 1024 ) //Sanity check: max 1GB
			throw new InvalidDataException( "LZ4 frame content size too large." );

		var output = new byte[contentSize];

		void* context = null;
		nuint written = 0;

		if ( Lz4.LZ4F_isError( Lz4.LZ4F_createDecompressionContext( &context, Lz4.LZ4F_VERSION ) ) == 0 )
		{
			written = (nuint)output.Length;
			var read = (nuint)data.Length;
			nuint result;

			fixed ( byte* srcPtr = data )
			fixed ( byte* dstPtr = output )
			{
				result = Lz4.LZ4F_decompress( context, dstPtr, &written, srcPtr, &read, null );
			}

			Lz4.LZ4F_freeDecompressionContext( context );

			//A frame that did not finish is a frame that was cut short - there is no half an answer to give, so nothing is what comes back.
			if ( Lz4.LZ4F_isError( result ) != 0 || result != 0 ) written = 0;
		}

		return output.AsSpan( 0, (int)written ).ToArray();
	}
}
