//The sRGB transfer curve in one place; standalone so overlay shaders needn't include frame.slang

#ifndef ENGINE_COLOUR_INCLUDED
#define ENGINE_COLOUR_INCLUDED

//sRGB to linear; call per vertex so gradients interpolate in linear light
float3 ToLinear( float3 colour )
{
	return select( colour <= 0.04045,
		colour / 12.92,
		pow( ( colour + 0.055 ) / 1.055, 2.4 ) );
}

//And light back into the numbers a person writes.
float3 ToGamma( float3 colour )
{
	colour = max( colour, 0.0 );

	return select( colour <= 0.0031308,
		colour * 12.92,
		1.055 * pow( colour, 1.0 / 2.4 ) - 0.055 );
}

//The same, for one number. The letters want it: a glyph's coverage was written in the space a reader sees and has to be judged there.
float ToSrgb( float light )
{
	return light <= 0.0031308
		? light * 12.92
		: 1.055 * pow( light, 1.0 / 2.4 ) - 0.055;
}

#endif
