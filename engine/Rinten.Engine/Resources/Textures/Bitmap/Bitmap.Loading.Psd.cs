using SkiaSharp;

namespace Rinten;

public partial class Bitmap
{
	/// <summary>Creates a Bitmap instance from PSD file data.</summary>
	public unsafe static Bitmap CreateFromPsdBytes( byte[] data )
	{
		if ( !IsPsd( data ) )
			return default;
			return default;
	}

	/// <summary>Checks if the provided byte array is a valid PSD file.</summary>
	public static bool IsPsd( byte[] data )
	{
		if ( data is null || data.Length < 26 ) return false;

		//PSD files start with the signature "8BPS" (38 42 50 53 in ASCII)
		return data[0] == '8' && data[1] == 'B' && data[2] == 'P' && data[3] == 'S';
	}
}

