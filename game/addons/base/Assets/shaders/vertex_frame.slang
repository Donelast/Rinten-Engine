//Unpacks the vertex normal/tangent word; must match VertexFrame in PackedVertex.cs

#ifndef ENGINE_VERTEX_FRAME
#define ENGINE_VERTEX_FRAME

static const uint EngineVertexBytes = 52;
static const uint EngineVertexColour = 12;
static const uint EngineVertexFrame = 16;
static const uint EngineVertexTexCoord0 = 20;
static const uint EngineVertexTexCoord1 = 36;

static const int VertexFrameHalf = 511;

//The decoded normal, and the two axes about it the tangent's angle is measured from - chosen by
//comparing whole numbers, so this and the processor that packed the word cannot choose differently.
void VertexFrameBasis( uint word, out float3 n, out float3 t1, out float3 t2 )
{
	int qa = int( word & 1023u ) - VertexFrameHalf;
	int qb = int( ( word >> 10 ) & 1023u ) - VertexFrameHalf;

	int x = qa + qb;
	int y = qa - qb;
	int z = 2 * ( VertexFrameHalf - max( abs( qa ), abs( qb ) ) );
	if ( ( word & ( 1u << 30 ) ) != 0u ) z = -z;

	n = normalize( float3( x, y, z ) );
	t1 = normalize( abs( x ) > abs( z ) ? float3( -y, x, 0 ) : float3( 0, -z, y ) );
	t2 = cross( n, t1 );
}

//The normal alone, which is most of what is asked.
float3 VertexFrameNormal( uint word )
{
	int qa = int( word & 1023u ) - VertexFrameHalf;
	int qb = int( ( word >> 10 ) & 1023u ) - VertexFrameHalf;

	float z = float( 2 * ( VertexFrameHalf - max( abs( qa ), abs( qb ) ) ) );
	return normalize( float3( float( qa + qb ), float( qa - qb ), ( word & ( 1u << 30 ) ) != 0u ? -z : z ) );
}

//The tangent, with the bitangent's handedness in w as the float vertex had it.
float4 VertexFrameTangent( uint word )
{
	float3 n, t1, t2;
	VertexFrameBasis( word, n, t1, t2 );

	float r = float( ( word >> 20 ) & 1023u ) * ( 1.0 / 256.0 );
	float x = r <= 2.0 ? 1.0 - r : r - 3.0;
	float y = ( r <= 2.0 ? 1.0 : -1.0 ) * ( 1.0 - abs( x ) );

	return float4( normalize( t1 * x + t2 * y ), ( word >> 31 ) != 0u ? -1.0 : 1.0 );
}

//Packs a normal and a tangent - for the skin pass, which poses them and writes them back. Neither needs to be a unit vector.
uint VertexFrameEncode( float3 normal, float4 tangent )
{
	float l1 = abs( normal.x ) + abs( normal.y ) + abs( normal.z );
	if ( !( l1 > 1e-30 ) ) { normal = float3( 0.0, 1.0, 0.0 ); l1 = 1.0; }

	float scale = float( VertexFrameHalf ) / l1;
	int qa = clamp( int( round( ( normal.x + normal.y ) * scale ) ), -VertexFrameHalf, VertexFrameHalf );
	int qb = clamp( int( round( ( normal.x - normal.y ) * scale ) ), -VertexFrameHalf, VertexFrameHalf );

	uint word = uint( qa + VertexFrameHalf ) | ( uint( qb + VertexFrameHalf ) << 10 );
	if ( normal.z < 0.0 && max( abs( qa ), abs( qb ) ) < VertexFrameHalf ) word |= 1u << 30;
	if ( tangent.w < 0.0 ) word |= 1u << 31;

	float3 n, t1, t2;
	VertexFrameBasis( word, n, t1, t2 );

	float c = dot( tangent.xyz, t1 );
	float s = dot( tangent.xyz, t2 );
	float around = abs( c ) + abs( s );

	uint turn = 0u;

	if ( around > 1e-30 )
	{
		float p = c / around;
		float r = s >= 0.0 ? 1.0 - p : 3.0 + p;
		turn = uint( int( round( r * 256.0 ) ) ) & 1023u;
	}

	return word | ( turn << 20 );
}

#endif
