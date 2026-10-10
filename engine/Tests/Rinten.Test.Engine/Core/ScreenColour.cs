using System;

namespace EngineTests;

//Expected bytes for a colour: views write to an sRGB-encoded format, not value * 255
internal static class ScreenColour
{
	//A linear value as the screen shows it.
	public static float Shown( float linear )
		=> linear <= 0.0031308f
			? linear * 12.92f
			: ( 1.055f * MathF.Pow( linear, 1.0f / 2.4f ) ) - 0.055f;

	//The same, as the byte it is read back as.
	public static byte Byte( float linear ) => (byte)MathF.Round( Shown( linear ) * 255.0f );

	//And from a byte value asked for in the same eight bits.
	public static byte Byte( int linear ) => Byte( linear / 255.0f );
}
