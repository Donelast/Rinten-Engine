//Kept as a file rather than as text inside the engine's C#, so it is compiled the way every other shader is - includes
//and all - and a project may put its own file of this name in its assets to replace it. See CoreShaderCache.Pass.

//Clustered light culling: per tile against side planes, then per cell against its view-space box

[[vk::binding(0,0)]] RWByteAddressBuffer g_lights;

[[vk::push_constant]]
cbuffer Push
{
	row_major float4x4 g_worldToView;

	//How wide the view is at unit depth, where it starts, and how many slices a factor of e of depth is worth.
	float4 g_screen;
	float4 g_cells;
	float4 g_where;
};

//How many lights one tile may carry into its slices.
static const uint MostPerTile = 64;

groupshared uint s_count;

//Where each of them is in view space, and how far it reaches - negative for the sun, which reaches everywhere.
groupshared float4 s_where[MostPerTile];
groupshared uint s_index[MostPerTile];

[shader("compute")]
[numthreads(64, 1, 1)]
void main( uint3 group : SV_GroupID, uint thread : SV_GroupIndex )
{
	uint cellsX = uint( g_cells.x );
	uint cellsY = uint( g_cells.y );
	uint cellsZ = uint( g_cells.z );
	if ( cellsX == 0 || cellsY == 0 || cellsZ == 0 ) return;

	uint tiles = cellsX * cellsY;
	if ( group.x >= tiles ) return;

	uint tx = group.x % cellsX;
	uint ty = group.x / cellsX;

	float hx = g_screen.x;
	float hy = g_screen.y;
	float near = g_screen.z;
	float scale = g_screen.w;
	//The tile is a column to infinity, because its last slice is.
	float far = 1e9;

	uint lightCount = uint( g_cells.w );
	uint perCell = uint( g_where.x );
	uint cellsAt = uint( g_where.y );

	//Directional lights go first, in order, unculled, so an overfull tile never drops the sun
	if ( thread == 0 )
	{
		s_count = 0;

		for ( uint sun = 0; sun < lightCount; ++sun )
		{
			if ( asfloat( g_lights.Load4( 64 + ( sun * 80 ) + 16 ) ).w >= 0.5 ) continue;
			if ( s_count >= MostPerTile ) break;

			s_where[s_count] = float4( 0.0, 0.0, 0.0, -1.0 );
			s_index[s_count] = sun;
			s_count++;
		}
	}

	GroupMemoryBarrierWithGroupSync();

	float x0 = ( float( tx ) / cellsX ) * 2.0 - 1.0;
	float x1 = ( float( tx + 1 ) / cellsX ) * 2.0 - 1.0;
	float y0 = ( float( ty ) / cellsY ) * 2.0 - 1.0;
	float y1 = ( float( ty + 1 ) / cellsY ) * 2.0 - 1.0;

	//The four sides of the tile, as planes through the eye. A point at depth d sits at ndc.x = p.x / ( hx * d ), so the edge at x0 is p.x + x0
	//* hx * p.z = nought - and which side of it is inside depends on the sign of hx, because the projection turns the picture over on its y.
	float sx = hx >= 0.0 ? 1.0 : -1.0;
	float sy = hy >= 0.0 ? 1.0 : -1.0;

	float3 planeA = normalize( float3( sx, 0.0, sx * x0 * hx ) );
	float3 planeB = normalize( float3( -sx, 0.0, -sx * x1 * hx ) );
	float3 planeC = normalize( float3( 0.0, sy, sy * y0 * hy ) );
	float3 planeD = normalize( float3( 0.0, -sy, -sy * y1 * hy ) );

	//What reaches this tile at all, decided once for the whole column rather than again in every
	//slice of it - and turned into view space here, which is the only matrix multiply a light gets.
	for ( uint i = thread; i < lightCount; i += 64 )
	{
		uint lightAt = 64 + ( i * 80 );
		float4 position = asfloat( g_lights.Load4( lightAt ) );
		float kind = asfloat( g_lights.Load4( lightAt + 16 ) ).w;

		//Already placed, above.
		if ( kind < 0.5 ) continue;

		float3 where = mul( float4( position.xyz, 1.0 ), g_worldToView ).xyz;
		float range = position.w;

		float deep = -where.z;
		if ( deep - range > far || deep + range < near ) continue;
		if ( dot( planeA, where ) < -range ) continue;
		if ( dot( planeB, where ) < -range ) continue;
		if ( dot( planeC, where ) < -range ) continue;
		if ( dot( planeD, where ) < -range ) continue;

		uint at = 0;
		InterlockedAdd( s_count, 1, at );
		if ( at >= MostPerTile ) continue;

		s_where[at] = float4( where, range );
		s_index[at] = i;
	}

	GroupMemoryBarrierWithGroupSync();

	uint kept = min( s_count, MostPerTile );

	//And then the slices of the column, a thread at a time, against what the tile kept.
	for ( uint cz = thread; cz < cellsZ; cz += 64 )
	{
		//Where this slice begins and ends, in metres from the eye. Even in the logarithm rather than
		//in the depth, so a cell near the camera is small and one far away is not needlessly so.
		float first = near * exp( float( cz ) / scale );
		float past = cz + 1 >= cellsZ ? 1e9 : near * exp( float( cz + 1 ) / scale );

		//The four corners of the cell at both depths; the view looks down its own negative z, so the near end is the larger.
		float2 a = float2( x0, y0 ) * float2( hx, hy ) * first;
		float2 b = float2( x0, y0 ) * float2( hx, hy ) * past;
		float2 c = float2( x1, y1 ) * float2( hx, hy ) * first;
		float2 d = float2( x1, y1 ) * float2( hx, hy ) * past;

		float3 boxMin = float3( min( min( a, b ), min( c, d ) ), -past );
		float3 boxMax = float3( max( max( a, b ), max( c, d ) ), -first );

		uint at = cellsAt + ( ( group.x + ( cz * tiles ) ) * ( perCell + 1 ) );
		uint got = 0;

		for ( uint k = 0; k < kept; ++k )
		{
			float4 one = s_where[k];

			if ( one.w >= 0.0 )
			{
				float3 nearest = clamp( one.xyz, boxMin, boxMax );
				float3 away = one.xyz - nearest;

				if ( dot( away, away ) > one.w * one.w ) continue;
			}

			if ( got < perCell ) g_lights.Store( ( at + 1 + got ) * 4, s_index[k] );
			got++;
		}

		g_lights.Store( at * 4, min( got, perCell ) );
	}
}
