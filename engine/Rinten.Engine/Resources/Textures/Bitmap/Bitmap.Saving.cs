using Rinten.Utility;
using SkiaSharp;

namespace Rinten;

public partial class Bitmap
{
	/// <summary>Exports the bitmap as a JPEG byte array with the specified quality.</summary>
	public byte[] ToJpg( int quality = 100 )
	{
		return Encode( SKEncodedImageFormat.Jpeg, quality );
	}

	/// <summary>Exports the bitmap as a PNG byte array.</summary>
	public byte[] ToPng()
	{
		return Encode( SKEncodedImageFormat.Png, 100 );
	}

	/// <summary>Exports the bitmap as a BMP byte array.</summary>
	public byte[] ToBmp()
	{
		return Encode( SKEncodedImageFormat.Bmp, 100 );
	}

	/// <summary>Exports the bitmap as an HDR WebP byte array with the specified quality.</summary>
	public byte[] ToWebP( int quality = 100 )
	{
		return Encode( SKEncodedImageFormat.Webp, quality );
	}

	//Exports the bitmap to the specified image format with optional quality.
	private byte[] Encode( SKEncodedImageFormat format, int quality )
	{
		using var image = SKImage.FromBitmap( _bitmap );
		using var data = image.Encode( format, quality );
		return data.ToArray();
	}

	/// <summary>Exports the bitmap to the specified engine format</summary>
	public byte[] ToFormat( ImageFormat format )
	{
		var data = _bitmap.GetPixels();

		//Skia may hand over its native order for a picture it decoded itself.
		var source = _bitmap.ColorType == SkiaSharp.SKColorType.Bgra8888 ? ImageFormat.BGRA8888 : ImageFormat;

		using var fbm = new FloatBitmap( Width, Height, source, data, ByteCount, srgb: false );
		return fbm.EncodeTo( format );
	}

}
