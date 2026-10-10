using System.Runtime.InteropServices;

namespace Rinten.CoreAssets;

//Transforms what a builder holds, in scalar maths to match the old C++ output byte for byte
internal sealed unsafe partial class ModelBuilder
{
	//Turns a vector by a quaternion.
	private static void Turn( ReadOnlySpan<float> q, float* v )
	{
		//The usual v + 2 * cross( q.xyz, cross( q.xyz, v ) + q.w * v ), written out because there is no vector type down here.
		var tx = 2.0f * ( ( q[1] * v[2] ) - ( q[2] * v[1] ) );
		var ty = 2.0f * ( ( q[2] * v[0] ) - ( q[0] * v[2] ) );
		var tz = 2.0f * ( ( q[0] * v[1] ) - ( q[1] * v[0] ) );

		var x = v[0] + ( q[3] * tx ) + ( ( q[1] * tz ) - ( q[2] * ty ) );
		var y = v[1] + ( q[3] * ty ) + ( ( q[2] * tx ) - ( q[0] * tz ) );
		var z = v[2] + ( q[3] * tz ) + ( ( q[0] * ty ) - ( q[1] * tx ) );

		v[0] = x;
		v[1] = y;
		v[2] = z;
	}

	//The same for one of the engine's vectors.
	private static void Turn( ReadOnlySpan<float> q, ref Vector3 vector )
	{
		var v = stackalloc float[3] { vector.x, vector.y, vector.z };

		Turn( q, v );

		vector = new Vector3( v[0], v[1], v[2] );
	}

	private static void Mat4Mul( ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> outMatrix )
	{
		var r = stackalloc float[16];

		for ( var row = 0; row < 4; ++row )
		{
			for ( var col = 0; col < 4; ++col )
			{
				r[( row * 4 ) + col] =
					( a[( row * 4 ) + 0] * b[( 0 * 4 ) + col] ) +
					( a[( row * 4 ) + 1] * b[( 1 * 4 ) + col] ) +
					( a[( row * 4 ) + 2] * b[( 2 * 4 ) + col] ) +
					( a[( row * 4 ) + 3] * b[( 3 * 4 ) + col] );
			}
		}

		for ( var i = 0; i < 16; ++i ) outMatrix[i] = r[i];
	}

	private static void InverseTrs( ReadOnlySpan<float> q, ReadOnlySpan<float> offset,
		ReadOnlySpan<float> scale, Span<float> outMatrix )
	{
		float x = q[0], y = q[1], z = q[2], w = q[3];
		float xx = x * x, yy = y * y, zz = z * z;
		float xy = x * y, xz = x * z, yz = y * z;
		float wx = w * x, wy = w * y, wz = w * z;

		var r00 = 1.0f - ( 2.0f * ( yy + zz ) );
		var r01 = 2.0f * ( xy - wz );
		var r02 = 2.0f * ( xz + wy );
		var r10 = 2.0f * ( xy + wz );
		var r11 = 1.0f - ( 2.0f * ( xx + zz ) );
		var r12 = 2.0f * ( yz - wx );
		var r20 = 2.0f * ( xz - wy );
		var r21 = 2.0f * ( yz + wx );
		var r22 = 1.0f - ( 2.0f * ( xx + yy ) );

		var isx = scale[0] != 0.0f ? 1.0f / scale[0] : 1.0f;
		var isy = scale[1] != 0.0f ? 1.0f / scale[1] : 1.0f;
		var isz = scale[2] != 0.0f ? 1.0f / scale[2] : 1.0f;

		outMatrix[0] = r00 * isx; outMatrix[1] = r01 * isy; outMatrix[2] = r02 * isz; outMatrix[3] = 0.0f;
		outMatrix[4] = r10 * isx; outMatrix[5] = r11 * isy; outMatrix[6] = r12 * isz; outMatrix[7] = 0.0f;
		outMatrix[8] = r20 * isx; outMatrix[9] = r21 * isy; outMatrix[10] = r22 * isz; outMatrix[11] = 0.0f;

		float px = -offset[0], py = -offset[1], pz = -offset[2];
		outMatrix[12] = ( px * outMatrix[0] ) + ( py * outMatrix[4] ) + ( pz * outMatrix[8] );
		outMatrix[13] = ( px * outMatrix[1] ) + ( py * outMatrix[5] ) + ( pz * outMatrix[9] );
		outMatrix[14] = ( px * outMatrix[2] ) + ( py * outMatrix[6] ) + ( pz * outMatrix[10] );
		outMatrix[15] = 1.0f;
	}

	private void ApplyInverseBind( ReadOnlySpan<float> left )
	{
		foreach ( var bone in bones ) Mat4Mul( left, bone.InverseBind, bone.InverseBind );
	}

	//Puts a run of vertices where the builder's placement says. Positions turn and then move;
	//normals and tangents only turn, because they are directions and a direction has nowhere to be.
	private void Place( int first, int count )
	{
		var all = CollectionsMarshal.AsSpan( vertices );

		for ( var i = 0; i < count; ++i )
		{
			ref var vertex = ref all[first + i];

			Turn( placementRotation, ref vertex.Position );

			vertex.Position.x += placementOffset[0];
			vertex.Position.y += placementOffset[1];
			vertex.Position.z += placementOffset[2];

			Turn( placementRotation, ref vertex.Normal );

			var tangent = new Vector3( vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z );
			Turn( placementRotation, ref tangent );

			vertex.Tangent = new Vector4( tangent.x, tangent.y, tangent.z, vertex.Tangent.w );
		}
	}

	private static void TransformPoint( ReadOnlySpan<float> q, ReadOnlySpan<float> offset,
		ReadOnlySpan<float> scale, float* v )
	{
		v[0] *= scale[0];
		v[1] *= scale[1];
		v[2] *= scale[2];

		Turn( q, v );

		v[0] += offset[0];
		v[1] += offset[1];
		v[2] += offset[2];
	}

	//Rotates a direction and scales by the inverse scale, then renormalises
	private static void TransformDirection( ReadOnlySpan<float> q, ReadOnlySpan<float> scale, float* v )
	{
		var isx = scale[0] != 0.0f ? 1.0f / scale[0] : 1.0f;
		var isy = scale[1] != 0.0f ? 1.0f / scale[1] : 1.0f;
		var isz = scale[2] != 0.0f ? 1.0f / scale[2] : 1.0f;

		v[0] *= isx;
		v[1] *= isy;
		v[2] *= isz;

		Turn( q, v );

		var length = MathF.Sqrt( ( v[0] * v[0] ) + ( v[1] * v[1] ) + ( v[2] * v[2] ) );

		if ( length > 1e-8f )
		{
			v[0] /= length;
			v[1] /= length;
			v[2] /= length;
		}
	}

	//Transforms a blend-shape delta: rotate and scale, no offset, never renormalised
	private static void TransformDelta( ReadOnlySpan<float> q, ReadOnlySpan<float> scale, float* v )
	{
		v[0] *= scale[0];
		v[1] *= scale[1];
		v[2] *= scale[2];

		Turn( q, v );
	}

	//The same for the normal a shape carries beside it - the inverse scale a normal takes, and
	//still no renormalising, because this is the difference between two normals rather than one.
	private static void TransformNormalDelta( ReadOnlySpan<float> q, ReadOnlySpan<float> scale, float* v )
	{
		v[0] *= scale[0] != 0.0f ? 1.0f / scale[0] : 1.0f;
		v[1] *= scale[1] != 0.0f ? 1.0f / scale[1] : 1.0f;
		v[2] *= scale[2] != 0.0f ? 1.0f / scale[2] : 1.0f;

		Turn( q, v );
	}

	//Hamilton product: turning by b and then by a, as one quaternion.
	private static void QuatMul( ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> outQuat )
	{
		var x = ( a[3] * b[0] ) + ( a[0] * b[3] ) + ( a[1] * b[2] ) - ( a[2] * b[1] );
		var y = ( a[3] * b[1] ) - ( a[0] * b[2] ) + ( a[1] * b[3] ) + ( a[2] * b[0] );
		var z = ( a[3] * b[2] ) + ( a[0] * b[1] ) - ( a[1] * b[0] ) + ( a[2] * b[3] );
		var w = ( a[3] * b[3] ) - ( a[0] * b[0] ) - ( a[1] * b[1] ) - ( a[2] * b[2] );

		outQuat[0] = x; outQuat[1] = y; outQuat[2] = z; outQuat[3] = w;
	}

	public void ApplyTransform( float* position, float* rotation, float* scale )
	{
		var identity = stackalloc float[4] { 0, 0, 0, 1 };
		var none = stackalloc float[3];
		var one = stackalloc float[3] { 1, 1, 1 };

		var q = new ReadOnlySpan<float>( rotation is not null ? rotation : identity, 4 );
		var o = new ReadOnlySpan<float>( position is not null ? position : none, 3 );
		var s = new ReadOnlySpan<float>( scale is not null ? scale : one, 3 );

		var all = CollectionsMarshal.AsSpan( vertices );
		var work = stackalloc float[3];

		for ( var i = 0; i < all.Length; i++ )
		{
			ref var vertex = ref all[i];

			work[0] = vertex.Position.x; work[1] = vertex.Position.y; work[2] = vertex.Position.z;
			TransformPoint( q, o, s, work );
			vertex.Position = new Vector3( work[0], work[1], work[2] );

			work[0] = vertex.Normal.x; work[1] = vertex.Normal.y; work[2] = vertex.Normal.z;
			TransformDirection( q, s, work );
			vertex.Normal = new Vector3( work[0], work[1], work[2] );

			work[0] = vertex.Tangent.x; work[1] = vertex.Tangent.y; work[2] = vertex.Tangent.z;
			TransformDirection( q, s, work );
			vertex.Tangent = new Vector4( work[0], work[1], work[2], vertex.Tangent.w );
		}

		//Blend-shape deltas take the turn and scale but not the offset, like normals
		foreach ( var morph in morphs )
		{
			var positions = CollectionsMarshal.AsSpan( morph.Positions );
			var normals = CollectionsMarshal.AsSpan( morph.Normals );

			fixed ( float* p = positions )
			{
				for ( var i = 0; i + 2 < positions.Length; i += 3 ) TransformDelta( q, s, p + i );
			}

			fixed ( float* n = normals )
			{
				for ( var i = 0; i + 2 < normals.Length; i += 3 ) TransformNormalDelta( q, s, n + i );
			}
		}

		Span<float> invT = stackalloc float[16];
		InverseTrs( q, o, s, invT );
		ApplyInverseBind( invT );

		foreach ( var body in bodies )
		{
			//Bone-attached shapes take only the scale (the bone already moved); boneless take the full transform
			var onBone = !string.IsNullOrEmpty( body.BoneName );

			var bq = onBone ? new ReadOnlySpan<float>( identity, 4 ) : q;
			var bo = onBone ? new ReadOnlySpan<float>( none, 3 ) : o;

			if ( onBone )
			{
				fixed ( float* transform = body.Transform )
				{
					TransformPoint( bq, bo, s, transform );

					Span<float> rotated = stackalloc float[4];
					QuatMul( bq, new ReadOnlySpan<float>( transform + 3, 4 ), rotated );

					for ( var i = 0; i < 4; i++ ) transform[3 + i] = rotated[i];
				}
			}

			foreach ( var shape in body.Shapes )
			{
				var points = CollectionsMarshal.AsSpan( shape.Points );

				fixed ( float* p = points )
				{
					for ( var i = 0; i + 2 < points.Length; i += 3 ) TransformPoint( bq, bo, s, p + i );
				}

				if ( shape.Kind is not ( ModelFormat.ShapeKind.Sphere or ModelFormat.ShapeKind.Capsule ) ) continue;

				fixed ( float* primitive = shape.Primitive )
				{
					TransformPoint( bq, bo, s, primitive );

					if ( shape.Kind == ModelFormat.ShapeKind.Capsule )
						TransformPoint( bq, bo, s, primitive + 3 );

					var avg = ( s[0] + s[1] + s[2] ) / 3.0f;

					if ( shape.Kind == ModelFormat.ShapeKind.Sphere ) primitive[3] *= avg;
					else primitive[6] *= avg;
				}
			}
		}
	}

	public void Mirror( int axis, int flipWinding, int remapLr )
	{
		var a = axis < 0 ? 0 : ( axis > 2 ? 2 : axis );

		var all = CollectionsMarshal.AsSpan( vertices );

		for ( var i = 0; i < all.Length; i++ )
		{
			ref var vertex = ref all[i];

			vertex.Position[a] = -vertex.Position[a];
			vertex.Normal[a] = -vertex.Normal[a];
			vertex.Tangent[a] = -vertex.Tangent[a];
		}

		Span<float> mirror = stackalloc float[16]
		{
			1, 0, 0, 0,
			0, 1, 0, 0,
			0, 0, 1, 0,
			0, 0, 0, 1
		};

		mirror[( a * 4 ) + a] = -1.0f;
		ApplyInverseBind( mirror );

		if ( flipWinding != 0 )
		{
			var flat = CollectionsMarshal.AsSpan( indices );

			foreach ( var mesh in meshes )
			{
				var first = (int)mesh.FirstIndex;
				var count = (int)mesh.IndexCount;

				for ( var i = 0; i + 2 < count; i += 3 )
				{
					if ( first + i + 2 >= flat.Length ) break;

					( flat[first + i], flat[first + i + 2] ) = ( flat[first + i + 2], flat[first + i] );
				}
			}
		}

		foreach ( var body in bodies )
		{
			body.Transform[a] = -body.Transform[a];

			foreach ( var shape in body.Shapes )
			{
				var points = shape.Points;

				for ( var i = 0; i + 2 < points.Count; i += 3 ) points[i + a] = -points[i + a];
			}
		}

		if ( remapLr == 0 ) return;

		foreach ( var bone in bones ) bone.Name = SwapLeftRight( bone.Name );
		foreach ( var body in bodies ) body.BoneName = SwapLeftRight( body.BoneName );
	}

	//A name with its side turned over - _L for _R , .l for .r , Left for Right - or the name it was given. Matched without case and replaced with the
	//spelling asked for, so a rig whose bones are hand_l comes out hand_R - which is what the C++ did, and what the tests that read these names expect.
	private static string SwapLeftRight( string name )
	{
		if ( TrySwap( ref name, "_L", "_R" ) ) return name;
		if ( TrySwap( ref name, ".l", ".r" ) ) return name;
		if ( TrySwap( ref name, "Left", "Right" ) ) return name;

		return name;
	}

	private static bool TrySwap( ref string name, string left, string right )
	{
		if ( name.Length >= left.Length && name.EndsWith( left, StringComparison.OrdinalIgnoreCase ) )
		{
			name = string.Concat( name.AsSpan( 0, name.Length - left.Length ), right );
			return true;
		}

		if ( name.Length >= left.Length && name.Length >= right.Length
			&& name.EndsWith( right, StringComparison.OrdinalIgnoreCase ) )
		{
			name = string.Concat( name.AsSpan( 0, name.Length - right.Length ), left );
			return true;
		}

		return false;
	}
}
