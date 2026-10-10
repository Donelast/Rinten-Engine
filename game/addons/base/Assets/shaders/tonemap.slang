//Tonemap curve used by the compose; a project overrides it with its own shaders/tonemap.slang

#ifndef ENGINE_TONEMAP_INCLUDED
#define ENGINE_TONEMAP_INCLUDED

//The ACES curve, in the fitted form everyone uses: a rational function that happens to sit
//very close to the real thing and costs five operations instead of a matrix and a spline.
float3 TonemapAces( float3 colour )
{
	const float a = 2.51;
	const float b = 0.03;
	const float c = 2.43;
	const float d = 0.59;
	const float e = 0.14;

	return saturate( ( colour * ( ( a * colour ) + b ) ) / ( ( colour * ( ( c * colour ) + d ) ) + e ) );
}

//John Hable's curve, from Uncharted 2 - the usual default, so an imported scene is expecting this shape.
float3 TonemapHable( float3 colour )
{
	const float a = 0.15;
	const float b = 0.50;
	const float c = 0.10;
	const float d = 0.20;
	const float e = 0.02;
	const float f = 0.30;

	float3 x = colour * 2.0;

	float3 mapped = ( ( x * ( ( a * x ) + ( c * b ) ) + ( d * e ) ) /
		( x * ( ( a * x ) + b ) + ( d * f ) ) ) - ( e / f );

	//The same curve applied to white, so that white comes out white rather than wherever the curve happens to put 11.2.
	const float w = 11.2;

	float white = ( ( w * ( ( a * w ) + ( c * b ) ) + ( d * e ) ) /
		( w * ( ( a * w ) + b ) + ( d * f ) ) ) - ( e / f );

	return saturate( mapped / white );
}

//Reinhard, with Jodie's fix: the plain curve desaturates bright colour towards white, and this one
//keeps the hue by mixing between the curve applied to the luminance and the curve applied per channel.
float3 TonemapReinhardJodie( float3 colour )
{
	float luma = dot( colour, float3( 0.2126, 0.7152, 0.0722 ) );

	float3 channels = colour / ( 1.0 + colour );
	float3 even = colour / ( 1.0 + luma );

	return saturate( lerp( even, channels, channels ) );
}

//AgX punchy look: log space and fitted contrast curve, still missing the two 3x3 matrices
float3 TonemapAgx( float3 colour )
{
	const float low = -12.47393;
	const float high = 4.026069;

	float3 x = saturate( ( log2( max( colour, 1e-10 ) ) - low ) / ( high - low ) );

	float3 x2 = x * x;
	float3 x4 = x2 * x2;

	float3 mapped = ( 15.5 * x4 * x2 ) - ( 40.14 * x4 * x ) + ( 31.96 * x4 )
		- ( 6.868 * x2 * x ) + ( 0.4298 * x2 ) + ( 0.1191 * x ) - 0.00232;

	mapped = saturate( mapped );

	//The punchy look: a little more contrast and a little more colour.
	mapped = pow( mapped, 1.0 / 1.35 );

	float luma = dot( mapped, float3( 0.2126, 0.7152, 0.0722 ) );

	return saturate( lerp( float3( luma, luma, luma ), mapped, 1.4 ) );
}

//Whichever of them the camera asked for. Nothing asked is ACES, which is the curve this
//always had - so a scene with no Tonemapping component on it comes out exactly as it did.
float3 EngineTonemap( float3 colour, float mode )
{
	if ( mode < 0.5 ) return TonemapAces( colour );          //nobody asked
	if ( mode < 1.5 ) return TonemapHable( colour );         //HableFilmic
	if ( mode < 2.5 ) return TonemapAces( colour );          //ACES
	if ( mode < 3.5 ) return TonemapReinhardJodie( colour ); //ReinhardJodie
	if ( mode < 4.5 ) return saturate( colour );             //Linear
	if ( mode < 5.5 ) return TonemapAgx( colour );           //AgX

	//Custom, and this is the engine's file - so there is no custom curve and the honest answer is the default one.
	return TonemapAces( colour );
}

#endif
