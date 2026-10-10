using SkiaSharp;

namespace Rinten.UI
{
	internal static class SkiaCompat
	{
		public static SKColor ToSk( this Color c )
		{
			var c32 = c.ToColor32();

			return new SKColor( c32.r, c32.g, c32.b, c32.a );
		}
	}

}
