//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Builds a picture's mip chain in one compute dispatch (average, or FOLD_MIN/FOLD_MAX for depth)

//The whole picture, read through the view over every level - in GENERAL, as the levels being stored into are.
[[vk::binding(0,1)]] Sampler2D g_source;

[[vk::binding(2,1)]] [format(FOLD_FORMAT)] RWTexture2D<float4> g_store0;
[[vk::binding(3,1)]] [format(FOLD_FORMAT)] RWTexture2D<float4> g_store1;
[[vk::binding(4,1)]] [format(FOLD_FORMAT)] RWTexture2D<float4> g_store2;
[[vk::binding(5,1)]] [format(FOLD_FORMAT)] RWTexture2D<float4> g_store3;
[[vk::binding(6,1)]] [format(FOLD_FORMAT)] RWTexture2D<float4> g_store4;
[[vk::binding(7,1)]] [format(FOLD_FORMAT)] RWTexture2D<float4> g_store5;

[[vk::push_constant]]
cbuffer Push
{
	uint g_pass;
	uint g_from;
	uint g_count;
	uint g_spare;

	//The picture's top, in texels.
	float2 g_size;
};

groupshared float3 s_tile[32][32];

//Four texels into one, as the build says.
float3 Fold4( float3 a, float3 b, float3 c, float3 d )
{
#if defined( FOLD_MIN )
	return min( min( a, b ), min( c, d ) );
#elif defined( FOLD_MAX )
	return max( max( a, b ), max( c, d ) );
#else
	return ( a + b + c + d ) * 0.25;
#endif
}

void Store( uint slot, uint2 at, float3 value )
{
	float4 written = float4( value, 1.0 );

	if ( slot == 1 ) g_store1[at] = written;
	else if ( slot == 2 ) g_store2[at] = written;
	else if ( slot == 3 ) g_store3[at] = written;
	else if ( slot == 4 ) g_store4[at] = written;
	else g_store5[at] = written;
}

[shader("compute")]
[numthreads(16, 16, 1)]
void main( uint3 group : SV_GroupID, uint3 thread : SV_GroupThreadID, uint3 id : SV_DispatchThreadID )
{
	{
		//First level via one filtered read, the rest in group memory; inline, Slang hangs on barrier helpers
		uint2 local = thread.xy * 2;
		uint2 origin = group.xy * 32;

		float2 fromSize = max( floor( g_size / exp2( float( g_from ) ) ), 1.0 );
		uint2 firstSize = uint2( max( floor( g_size / exp2( float( g_from + 1 ) ) ), 1.0 ) );

		for ( uint dy = 0; dy < 2; dy++ )
			for ( uint dx = 0; dx < 2; dx++ )
			{
				uint2 at = origin + local + uint2( dx, dy );
				float3 value = float3( 0.0, 0.0, 0.0 );

				if ( at.x < firstSize.x && at.y < firstSize.y )
				{
#if defined( FOLD_MIN ) || defined( FOLD_MAX )
					float2 corner = float2( at ) * 2.0;
					float level = float( g_from );
					value = Fold4(
						g_source.SampleLevel( ( corner + float2( 0.5, 0.5 ) ) / fromSize, level ).rgb,
						g_source.SampleLevel( ( corner + float2( 1.5, 0.5 ) ) / fromSize, level ).rgb,
						g_source.SampleLevel( ( corner + float2( 0.5, 1.5 ) ) / fromSize, level ).rgb,
						g_source.SampleLevel( ( corner + float2( 1.5, 1.5 ) ) / fromSize, level ).rgb );
#else
					float2 uv = ( float2( at ) * 2.0 + 1.0 ) / fromSize;
					value = g_source.SampleLevel( uv, float( g_from ) ).rgb;
#endif
					g_store0[at] = float4( value, 1.0 );
				}

				s_tile[local.y + dy][local.x + dx] = value;
			}

		uint side = 16;

		for ( uint level = 1; level < g_count; level++ )
		{
			GroupMemoryBarrierWithGroupSync();

			bool mine = thread.x < side && thread.y < side;
			float3 value = float3( 0.0, 0.0, 0.0 );

			if ( mine )
			{
				uint2 at = thread.xy * 2;
				value = Fold4( s_tile[at.y][at.x], s_tile[at.y][at.x + 1],
					s_tile[at.y + 1][at.x], s_tile[at.y + 1][at.x + 1] );
			}

			GroupMemoryBarrierWithGroupSync();

			if ( mine )
			{
				s_tile[thread.y][thread.x] = value;

				uint2 at = ( group.xy * side ) + thread.xy;
				uint2 size = uint2( max( floor( g_size / exp2( float( g_from + 1 + level ) ) ), 1.0 ) );

				if ( at.x < size.x && at.y < size.y ) Store( level, at, value );
			}

			side >>= 1;
		}

		return;
	}
}
