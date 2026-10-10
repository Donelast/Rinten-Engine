using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rinten;

//The 52-byte vertex as stored and read by the GPU; normal and tangent packed in one VertexFrame word
[StructLayout( LayoutKind.Sequential, Pack = 4 )]
internal struct PackedVertex
{
	public Vector3 Position;
	public Color32 Color;

	/// <summary>The normal, the tangent and its handedness - see <see cref="VertexFrame"/>.</summary>
	public uint Frame;

	public Vector4 TexCoord0;
	public Vector4 TexCoord1;

	/// <summary>Where each field sits, which the shaders and the pipelines spell too.</summary>
	public const int FrameOffset = 16;
	public const int TexCoord0Offset = 20;
	public const int TexCoord1Offset = 36;
	public const int Size = 52;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static PackedVertex From( in Vertex v ) => new()
	{
		Position = v.Position,
		Color = v.Color,
		Frame = VertexFrame.Encode( v.Normal, v.Tangent ),
		TexCoord0 = v.TexCoord0,
		TexCoord1 = v.TexCoord1,
	};

	public readonly Vertex ToVertex()
	{
		VertexFrame.Decode( Frame, out var normal, out var tangent );

		return new Vertex
		{
			Position = Position,
			Color = Color,
			Normal = normal,
			Tangent = tangent,
			TexCoord0 = TexCoord0,
			TexCoord1 = TexCoord1,
		};
	}

	/// <summary>A run of vertices packed into another.</summary>
	public static void Pack( ReadOnlySpan<Vertex> from, Span<PackedVertex> into )
	{
		for ( var i = 0; i < from.Length; i++ ) into[i] = From( from[i] );
	}

	/// <summary>And back.</summary>
	public static void Unpack( ReadOnlySpan<PackedVertex> from, Span<Vertex> into )
	{
		for ( var i = 0; i < from.Length; i++ ) into[i] = from[i].ToVertex();
	}
}

//Normal, tangent and handedness in 32 bits: octahedral normal plus tangent angle around it
//Must match vertex_frame.slang exactly
internal static class VertexFrame
{
	private const int Half = 511;

	//The frame of a vertex that said nothing - up, and the tangent the axes make of it. Most of what is
	//drawn immediately (lines, gizmos, text) says nothing, and is handed this without the arithmetic.
	private static readonly uint Nothing = EncodeWhole( 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f );

	/// <summary>Packs a normal and a tangent. Neither needs to be a unit vector; a normal of nothing reads as up, and a tangent of nothing as whatever the normal's own axes make it.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static uint Encode( in Vector3 normal, in Vector4 tangent )
	{
		if ( normal.x == 0.0f && normal.y == 0.0f && normal.z == 0.0f && tangent.x == 0.0f && tangent.y == 0.0f && tangent.z == 0.0f )
			return tangent.w < 0.0f ? Nothing | ( 1u << 31 ) : Nothing;

		return EncodeWhole( normal.x, normal.y, normal.z, tangent.x, tangent.y, tangent.z, tangent.w );
	}

	private static uint EncodeWhole( float nx, float ny, float nz, float tx, float ty, float tz, float tw )
	{
		var l1 = MathF.Abs( nx ) + MathF.Abs( ny ) + MathF.Abs( nz );

		//Nothing, or not a number: up.
		if ( !( l1 > 1e-30f ) || float.IsInfinity( l1 ) ) { nx = 0.0f; ny = 1.0f; nz = 0.0f; l1 = 1.0f; }

		var scale = Half / l1;
		var qa = Clamp( (int)MathF.Round( ( nx + ny ) * scale ) );
		var qb = Clamp( (int)MathF.Round( ( nx - ny ) * scale ) );

		var word = (uint)( qa + Half ) | ( (uint)( qb + Half ) << 10 );

		//On the rim the side is nobody's, and saying nothing keeps one encoding for it.
		if ( nz < 0.0f && Math.Max( Math.Abs( qa ), Math.Abs( qb ) ) < Half ) word |= 1u << 30;
		if ( tw < 0.0f ) word |= 1u << 31;

		//The tangent, as an angle about the normal the card will decode - not the one that was asked for, which it will never see.
		Basis( word, out _, out var t1, out var t2 );

		var c = ( tx * t1.x ) + ( ty * t1.y ) + ( tz * t1.z );
		var s = ( tx * t2.x ) + ( ty * t2.y ) + ( tz * t2.z );
		var around = MathF.Abs( c ) + MathF.Abs( s );

		uint turn = 0;

		if ( around > 1e-30f )
		{
			var p = c / around;
			var r = s >= 0.0f ? 1.0f - p : 3.0f + p;
			turn = (uint)(int)MathF.Round( r * 256.0f ) & 1023u;
		}

		return word | ( turn << 20 );
	}

	/// <summary>Unpacks a word into a unit normal and a unit tangent with its handedness in w.</summary>
	public static void Decode( uint word, out Vector3 normal, out Vector4 tangent )
	{
		Basis( word, out var n, out var t1, out var t2 );

		var r = ( ( word >> 20 ) & 1023u ) * ( 1.0f / 256.0f );
		var x = r <= 2.0f ? 1.0f - r : r - 3.0f;
		var y = ( r <= 2.0f ? 1.0f : -1.0f ) * ( 1.0f - MathF.Abs( x ) );

		var t = ( t1 * x ) + ( t2 * y );

		normal = n;
		tangent = new Vector4( t.Normal, ( word >> 31 ) != 0 ? -1.0f : 1.0f );
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static int Clamp( int q ) => q < -Half ? -Half : q > Half ? Half : q;

	//The decoded normal and the two axes around it the tangent's angle is measured from.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static void Basis( uint word, out Vector3 n, out Vector3 t1, out Vector3 t2 )
	{
		var qa = (int)( word & 1023u ) - Half;
		var qb = (int)( ( word >> 10 ) & 1023u ) - Half;

		//The normal in whole numbers, unnormalised: x = a + b, y = a - b, and z whatever of the octahedron is left, on the side the bit says.
		var x = qa + qb;
		var y = qa - qb;
		var z = 2 * ( Half - Math.Max( Math.Abs( qa ), Math.Abs( qb ) ) );
		if ( ( word & ( 1u << 30 ) ) != 0 ) z = -z;

		n = new Vector3( x, y, z ).Normal;

		//Chosen on the whole numbers, so the processor and the card choose alike.
		t1 = Math.Abs( x ) > Math.Abs( z ) ? new Vector3( -y, x, 0 ).Normal : new Vector3( 0, -z, y ).Normal;
		t2 = Vector3.Cross( n, t1 );
	}
}
