using System.Runtime.InteropServices;

namespace Rinten.Native;

//LZ4 block and frame C APIs from rintencore; frame structs match the C header layout
internal static unsafe partial class Lz4
{
	private const string Library = "rintencore";

	/// <summary>LZ4F_VERSION: what every frame context is asked for, and refused without.</summary>
	public const uint LZ4F_VERSION = 100;

	[StructLayout( LayoutKind.Sequential )]
	public struct LZ4F_frameInfo_t
	{
		public int blockSizeID;
		public int blockMode;
		public int contentChecksumFlag;
		public int frameType;
		public ulong contentSize;
		public uint dictID;
		public int blockChecksumFlag;
	}

	[StructLayout( LayoutKind.Sequential )]
	public struct LZ4F_preferences_t
	{
		public LZ4F_frameInfo_t frameInfo;
		public int compressionLevel;
		public uint autoFlush;
		public uint favorDecSpeed;
		public fixed uint reserved[3];
	}

	//-- blocks -----------------------------------------------------------------

	[LibraryImport( Library )]
	public static partial int LZ4_compressBound( int inputSize );

	[LibraryImport( Library )]
	public static partial int LZ4_compress_default( byte* src, byte* dst, int srcSize, int dstCapacity );

	[LibraryImport( Library )]
	public static partial int LZ4_compress_HC( byte* src, byte* dst, int srcSize, int dstCapacity, int compressionLevel );

	[LibraryImport( Library )]
	public static partial int LZ4_decompress_safe( byte* src, byte* dst, int compressedSize, int dstCapacity );

	//-- frames -----------------------------------------------------------------

	[LibraryImport( Library )]
	public static partial uint LZ4F_isError( nuint code );

	[LibraryImport( Library )]
	public static partial nuint LZ4F_compressFrameBound( nuint srcSize, LZ4F_preferences_t* preferencesPtr );

	[LibraryImport( Library )]
	public static partial nuint LZ4F_compressFrame( void* dstBuffer, nuint dstCapacity,
		void* srcBuffer, nuint srcSize, LZ4F_preferences_t* preferencesPtr );

	[LibraryImport( Library )]
	public static partial nuint LZ4F_createDecompressionContext( void** dctxPtr, uint version );

	[LibraryImport( Library )]
	public static partial nuint LZ4F_freeDecompressionContext( void* dctx );

	[LibraryImport( Library )]
	public static partial nuint LZ4F_getFrameInfo( void* dctx, LZ4F_frameInfo_t* frameInfoPtr,
		void* srcBuffer, nuint* srcSizePtr );

	[LibraryImport( Library )]
	public static partial nuint LZ4F_decompress( void* dctx, void* dstBuffer, nuint* dstSizePtr,
		void* srcBuffer, nuint* srcSizePtr, void* dOptPtr );
}
