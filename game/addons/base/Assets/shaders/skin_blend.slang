//Blends four bones from a palette of matrices (linear) or dual quaternions (keeps volume)

//A matrix out of a unit rotation, a translation and a scale - the row-vector kind,
//scaled first: System.Numerics' CreateFromQuaternion, rows scaled, translation last.
float4x4 SkinMatrix( float4 q, float3 translation, float3 scale )
{
	float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
	float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
	float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;

	return float4x4(
		float4( float3( 1.0 - 2.0 * ( yy + zz ), 2.0 * ( xy + wz ), 2.0 * ( xz - wy ) ) * scale.x, 0.0 ),
		float4( float3( 2.0 * ( xy - wz ), 1.0 - 2.0 * ( xx + zz ), 2.0 * ( yz + wx ) ) * scale.y, 0.0 ),
		float4( float3( 2.0 * ( xz + wy ), 2.0 * ( yz - wx ), 1.0 - 2.0 * ( xx + yy ) ) * scale.z, 0.0 ),
		float4( translation, 1.0 ) );
}

//The blend of four palette entries by four weights, as one matrix.
float4x4 SkinBlend( float4x4 a, float4x4 b, float4x4 c, float4x4 d, float4 weights )
{
	if ( a[3][3] < 1.5 )
		return ( a * weights.x ) + ( b * weights.y ) + ( c * weights.z ) + ( d * weights.w );

	//Each rotation on the same side of the sphere as the first before they are added: q and
	//-q are one rotation, and adding opposite ones would cancel to nothing half way round.
	float4 real = a[0] * weights.x;
	float4 dual = a[1] * weights.x;

	float sb = dot( a[0], b[0] ) < 0.0 ? -weights.y : weights.y;
	float sc = dot( a[0], c[0] ) < 0.0 ? -weights.z : weights.z;
	float sd = dot( a[0], d[0] ) < 0.0 ? -weights.w : weights.w;

	real += ( b[0] * sb ) + ( c[0] * sc ) + ( d[0] * sd );
	dual += ( b[1] * sb ) + ( c[1] * sc ) + ( d[1] * sd );

	float3 scale = ( a[2].xyz * weights.x ) + ( b[2].xyz * weights.y ) + ( c[2].xyz * weights.z ) + ( d[2].xyz * weights.w );

	float length = max( sqrt( dot( real, real ) ), 1e-8 );
	real /= length;
	dual /= length;

	//The translation: twice the vector part of the dual part times the rotation's conjugate.
	float3 translation = 2.0 * ( ( real.w * dual.xyz ) - ( dual.w * real.xyz ) + cross( real.xyz, dual.xyz ) );

	return SkinMatrix( real, translation, scale );
}
