//Engine pass that fills the sky ambient table from a sky's SkyLight( height, mu ) function

struct SkyTableV2F
{
	float4 position : SV_Position;
};

[shader("vertex")]
SkyTableV2F MainSkyTableVs( uint id : SV_VertexID )
{
	//One triangle covering the table: (-1,-1), (3,-1), (-1,3) in clip space.
	float2 corner = float2( ( id << 1 ) & 2, id & 2 );

	SkyTableV2F o;
	o.position = float4( ( corner * 2.0 ) - 1.0, 0.0, 1.0 );
	return o;
}

[shader("fragment")]
float4 MainSkyTablePs( SkyTableV2F i ) : SV_Target0
{
	//Texel centres stand for nought to one inclusive, which is how SkyTableUv in frame.slang maps a direction onto them.
	float2 at = floor( i.position.xy ) / ( SkyTableSide - 1.0 );

	//Rows are spaced by the square root of height so they cluster near the horizon
	float up = at.y * at.y;

	//One in the alpha, which is what says a table was drawn at all - a picture nobody drew into reads
	//nought there, and the portable frame's read takes that as "this frame has no table". See SkyTableAt.
	return float4( SkyLight( ( up * 2.0 ) - 1.0, at.x ), 1.0 );
}
