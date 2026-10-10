//Unpacks a half float with 32-bit math; f16tof32 needs 16-bit types that WebGL lacks
#pragma once

//The half float held in the low sixteen bits of <paramref name="bits"/>.
float HalfToFloat( uint bits )
{
	uint exponent = ( bits >> 10 ) & 0x1fu;
	uint mantissa = bits & 0x3ffu;

	//A subnormal half is the mantissa in units of 2^-24; everything else is the same fraction with the exponent
	//rebased from fifteen to a hundred and twenty-seven. The top exponent - infinity, not-a-number - is never packed.
	float value = exponent == 0u
		? float( mantissa ) * 5.9604644775390625e-8
		: asfloat( ( ( exponent + 112u ) << 23 ) | ( mantissa << 13 ) );

	return ( bits & 0x8000u ) != 0u ? -value : value;
}
