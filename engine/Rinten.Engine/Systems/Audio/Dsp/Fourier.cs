namespace Rinten.Audio;

//In-place, allocation-free DFT for spectrum analysis; safe to call on the mixing thread
internal static class Fourier
{
	/// <summary>An in-place radix-two transform: the bits reversed, then the butterflies. Both spans are the same power-of-two length; the imaginary half is usually zeros on the way in.</summary>
	public static void Transform( Span<float> re, Span<float> im )
	{
		var n = re.Length;

		if ( im.Length != n || n < 2 || ( n & ( n - 1 ) ) != 0 )
			throw new ArgumentException( "The transform takes two spans of the same power-of-two length" );

		for ( int i = 1, j = 0; i < n; ++i )
		{
			var bit = n >> 1;
			for ( ; ( j & bit ) != 0; bit >>= 1 ) j ^= bit;
			j ^= bit;

			if ( i < j )
			{
				( re[i], re[j] ) = ( re[j], re[i] );
				( im[i], im[j] ) = ( im[j], im[i] );
			}
		}

		for ( var length = 2; length <= n; length <<= 1 )
		{
			var angle = -2.0f * MathF.PI / length;
			var stepRe = MathF.Cos( angle );
			var stepIm = MathF.Sin( angle );

			for ( var start = 0; start < n; start += length )
			{
				var wRe = 1.0f;
				var wIm = 0.0f;

				for ( var k = 0; k < length / 2; ++k )
				{
					var a = start + k;
					var b = a + ( length / 2 );

					var tRe = ( re[b] * wRe ) - ( im[b] * wIm );
					var tIm = ( re[b] * wIm ) + ( im[b] * wRe );

					re[b] = re[a] - tRe;
					im[b] = im[a] - tIm;
					re[a] += tRe;
					im[a] += tIm;

					var next = ( wRe * stepRe ) - ( wIm * stepIm );
					wIm = ( wRe * stepIm ) + ( wIm * stepRe );
					wRe = next;
				}
			}
		}
	}

	/// <summary>A Hann window of <paramref name="size"/> points: what a buffer is multiplied by before the transform, so that its two cut ends do not read as a click spread across every frequency.</summary>
	public static float[] Hann( int size )
	{
		var made = new float[size];

		for ( var i = 0; i < size; ++i )
			made[i] = 0.5f - ( 0.5f * MathF.Cos( 2.0f * MathF.PI * i / ( size - 1 ) ) );

		return made;
	}
}
