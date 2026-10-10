using System.Runtime.InteropServices;

namespace Rinten;

//Basis transcoder (bt_* in rintencore): KTX2 UASTC to ASTC, ETC2, BC7 or RGBA8 per device support
internal static partial class CoreBasisTranscoder
{
	private const string Library = "rintencore";

	//basist::transcoder_texture_format
	internal const uint Etc2Rgba = 1;
	internal const uint Bc7Rgba = 6;
	internal const uint Astc4x4Rgba = 10;
	internal const uint Rgba32 = 13;

	[LibraryImport( Library )]
	private static partial void bt_init();

	[LibraryImport( Library )]
	private static partial ulong bt_ktx2_open( ulong data, uint length );

	[LibraryImport( Library )]
	private static partial void bt_ktx2_close( ulong handle );

	[LibraryImport( Library )]
	private static partial uint bt_ktx2_start_transcoding( ulong handle );

	[LibraryImport( Library )]
	private static partial uint bt_ktx2_get_level_actual_width( ulong handle, uint level, uint layer, uint face );

	[LibraryImport( Library )]
	private static partial uint bt_ktx2_get_level_actual_height( ulong handle, uint level, uint layer, uint face );

	[LibraryImport( Library )]
	private static partial uint bt_basis_compute_transcoded_image_size_in_bytes( uint format, uint width, uint height );

	[LibraryImport( Library )]
	private static partial uint bt_ktx2_transcode_image_level( ulong handle, uint level, uint layer, uint face,
		ulong output, uint outputBlocksOrPixels, uint format, uint decodeFlags, uint rowPitch, uint rows,
		int channel0, int channel1, ulong state );

	private static readonly object gate = new();
	private static bool? available;

	/// <summary>Whether this rintencore carries the transcoder.</summary>
	public static bool Available
	{
		get
		{
			lock ( gate )
			{
				if ( available is { } known ) return known;

				try
				{
					bt_init();
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

	//Bytes a block of a format, or of a pixel where it is not one of blocks.
	internal static int BlockBytes( uint format ) => format == Rgba32 ? 4 : 16;

	/// <summary>Every level of <paramref name="picture"/> as <paramref name="format"/>, back to back, largest first: each level's width, height and byte count in <paramref name="levels"/>. Null where it would not transcode.</summary>
	public static unsafe byte[] Transcode( CoreUniversalTexture picture, uint format, out int[] levels )
	{
		levels = null;
		if ( picture?.Bytes is not { Length: > 0 } bytes || !Available ) return null;

		fixed ( byte* data = bytes )
		{
			var handle = bt_ktx2_open( (ulong)data, (uint)bytes.Length );
			if ( handle == 0 ) return null;

			try
			{
				if ( bt_ktx2_start_transcoding( handle ) == 0 ) return null;

				var count = picture.Levels;
				var info = new int[count * 3];
				var total = 0;

				for ( var level = 0; level < count; level++ )
				{
					var width = bt_ktx2_get_level_actual_width( handle, (uint)level, 0, 0 );
					var height = bt_ktx2_get_level_actual_height( handle, (uint)level, 0, 0 );
					var size = (int)bt_basis_compute_transcoded_image_size_in_bytes( format, width, height );

					info[level * 3] = (int)width;
					info[( level * 3 ) + 1] = (int)height;
					info[( level * 3 ) + 2] = size;
					total += size;
				}

				var output = new byte[total];
				var at = 0;

				fixed ( byte* into = output )
				{
					for ( var level = 0; level < count; level++ )
					{
						var size = info[( level * 3 ) + 2];
						var units = (uint)( size / BlockBytes( format ) );

						if ( bt_ktx2_transcode_image_level( handle, (uint)level, 0, 0, (ulong)( into + at ), units,
							format, 0, 0, 0, -1, -1, 0 ) == 0 )
						{
							return null;
						}

						at += size;
					}
				}

				levels = info;
				return output;
			}
			finally
			{
				bt_ktx2_close( handle );
			}
		}
	}
}
