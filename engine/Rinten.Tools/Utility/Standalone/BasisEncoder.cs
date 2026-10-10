using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Editor;

//Basis Universal encoder from rintencore (editor only): picture levels in, KTX2 UASTC out for web
internal static unsafe partial class BasisEncoder
{
	private const string Library = "rintencore";

	//basist::basis_tex_format
	private const uint UastcLdr4x4 = 1;

	//basisu::basis_compressor flags - see basisu_wasm_api_common.h.
	private const ulong FlagThreaded = 1 << 9;
	private const ulong FlagKtx2 = 1 << 11;
	private const ulong FlagKtx2Zstd = 1 << 12;
	private const ulong FlagSrgb = 1 << 13;

	//cPackUASTCLevelDefault: the encoder's own middle, between its fastest and its slowest.
	private const ulong PackLevelDefault = 2;

	//How hard the rate-distortion pass leans towards fewer distinct blocks - which zstd then finds - against error. The encoder's own default.
	private const float RateDistortion = 1.0f;

	[LibraryImport( Library )]
	private static partial void bu_init();

	[LibraryImport( Library )]
	private static partial ulong bu_new_comp_params();

	[LibraryImport( Library )]
	private static partial uint bu_delete_comp_params( ulong parameters );

	[LibraryImport( Library )]
	private static partial uint bu_comp_params_set_image_rgba32( ulong parameters, uint image, ulong pixels, uint width, uint height, uint pitch );

	[LibraryImport( Library )]
	private static partial uint bu_compress_texture( ulong parameters, uint format, int qualityLevel, int effortLevel, ulong flags, float rateDistortion );

	[LibraryImport( Library )]
	private static partial ulong bu_comp_params_get_comp_data_size( ulong parameters );

	[LibraryImport( Library )]
	private static partial ulong bu_comp_params_get_comp_data_ofs( ulong parameters );

	private static readonly object gate = new();
	private static bool? available;

	/// <summary>Whether this editor's rintencore carries the encoder - a copy built before it came in has not, and a build then carries its pictures as pictures.</summary>
	public static bool Available
	{
		get
		{
			lock ( gate )
			{
				if ( available is { } known ) return known;

				try
				{
					bu_init();
					available = true;
				}
				catch ( Exception e ) when ( e is DllNotFoundException or EntryPointNotFoundException )
				{
					available = false;
				}

				return available.Value;
			}
		}
	}

	/// <summary>A KTX2 of UASTC blocks from a picture's levels, largest first, four bytes a pixel each - the levels as the engine makes them, so the cut's coverage and everything else it keeps from one level to the next is what the blocks hold. Null where the encoder refused.</summary>
	public static byte[] EncodeUastc( IReadOnlyList<(byte[] Pixels, int Width, int Height)> levels, bool srgb )
	{
		if ( levels is null || levels.Count == 0 || !Available ) return null;

		var parameters = bu_new_comp_params();
		if ( parameters == 0 ) return null;

		var pinned = new GCHandle[levels.Count];

		try
		{
			for ( var i = 0; i < levels.Count; i++ )
			{
				var (pixels, width, height) = levels[i];
				if ( pixels is null || pixels.Length < width * height * 4 ) return null;

				pinned[i] = GCHandle.Alloc( pixels, GCHandleType.Pinned );

				if ( bu_comp_params_set_image_rgba32( parameters, (uint)i, (ulong)pinned[i].AddrOfPinnedObject(), (uint)width, (uint)height, (uint)( width * 4 ) ) == 0 )
					return null;
			}

			//Several images are taken as a picture's levels, and none is made by the encoder - see basis_compress_internal.
			var flags = PackLevelDefault | FlagThreaded | FlagKtx2 | FlagKtx2Zstd | ( srgb ? FlagSrgb : 0 );

			if ( bu_compress_texture( parameters, UastcLdr4x4, -1, -1, flags, RateDistortion ) == 0 )
				return null;

			var size = bu_comp_params_get_comp_data_size( parameters );
			var data = bu_comp_params_get_comp_data_ofs( parameters );
			if ( size == 0 || data == 0 ) return null;

			return new ReadOnlySpan<byte>( (void*)data, checked((int)size) ).ToArray();
		}
		finally
		{
			foreach ( var handle in pinned )
			{
				if ( handle.IsAllocated ) handle.Free();
			}

			bu_delete_comp_params( parameters );
		}
	}
}
