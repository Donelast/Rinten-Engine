//Texture size in texels; on WGSL asks the texture directly, as Slang botches combined samplers
#pragma once

uint2 PictureSize( Sampler2D t )
{
	__target_switch
	{
	case wgsl:
		__intrinsic_asm "vec2<u32>(textureDimensions($0))";
	default:
		uint width, height;
		t.GetDimensions( width, height );
		return uint2( width, height );
	}
}
