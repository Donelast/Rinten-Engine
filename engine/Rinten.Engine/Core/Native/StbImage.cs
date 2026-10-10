using System.Runtime.InteropServices;

namespace Rinten.Native;

//stb_image from rintencore (image.c): PNG, JPEG, TGA, BMP, HDR decoding
internal static unsafe partial class StbImage
{
	private const string Library = "rintencore";

	/// <summary>Decodes an image held in memory. Returns pixels the library owns - hand them back with <see cref="stbi_image_free"/> - or null, with the reason in <see cref="stbi_failure_reason"/>.</summary>
	[LibraryImport( Library )]
	public static partial byte* stbi_load_from_memory( byte* buffer, int len, int* x, int* y, int* channelsInFile, int desiredChannels );

	[LibraryImport( Library )]
	public static partial void stbi_image_free( void* retvalFromLoad );

	[LibraryImport( Library )]
	public static partial byte* stbi_failure_reason();
}
