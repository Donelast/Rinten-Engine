namespace Rinten.Audio;

//Distance and occlusion as low/mid/high gains via two shelf filters, smoothed per sample
internal sealed class DirectEffect
{
	//Where the low shelf gives way to the middle.
	private const float LowCrossover = 250.0f;

	//And where the middle gives way to the high.
	private const float HighCrossover = 4000.0f;

	//One biquad, per channel, in the usual direct form.
	private struct Biquad
	{
		public float B0, B1, B2, A1, A2;

		public float X1, X2, Y1, Y2;

		public void Reset()
		{
			X1 = 0.0f;
			X2 = 0.0f;
			Y1 = 0.0f;
			Y2 = 0.0f;
		}

		public float Process( float input )
		{
			var output = ( B0 * input ) + ( B1 * X1 ) + ( B2 * X2 ) - ( A1 * Y1 ) - ( A2 * Y2 );

			X2 = X1;
			X1 = input;

			Y2 = Y1;
			Y1 = output;

			return output;
		}

		//The usual cookbook shelf, with the slope left at one - a steeper shelf rings, and what is being modelled here is air, which does not.
		/// <summary>A shelf: everything past the corner is turned by a gain, everything before it is left alone.</summary>
		public void SetShelf( float frequency, float gain, float rate, bool high )
		{
			gain = MathF.Max( gain, 0.0001f );

			var a = MathF.Sqrt( gain );
			var w = 2.0f * MathF.PI * frequency / rate;

			var cos = MathF.Cos( w );
			var sin = MathF.Sin( w );

			//Slope of one, which is where the square root of two comes from.
			var alpha = sin / 2.0f * MathF.Sqrt( ( a + ( 1.0f / a ) ) + 2.0f );

			var twoRootAlpha = 2.0f * MathF.Sqrt( a ) * alpha;

			float b0, b1, b2, a0, a1, a2;

			if ( high )
			{
				b0 = a * ( ( a + 1.0f ) + ( ( a - 1.0f ) * cos ) + twoRootAlpha );
				b1 = -2.0f * a * ( ( a - 1.0f ) + ( ( a + 1.0f ) * cos ) );
				b2 = a * ( ( a + 1.0f ) + ( ( a - 1.0f ) * cos ) - twoRootAlpha );

				a0 = ( a + 1.0f ) - ( ( a - 1.0f ) * cos ) + twoRootAlpha;
				a1 = 2.0f * ( ( a - 1.0f ) - ( ( a + 1.0f ) * cos ) );
				a2 = ( a + 1.0f ) - ( ( a - 1.0f ) * cos ) - twoRootAlpha;
			}
			else
			{
				b0 = a * ( ( a + 1.0f ) - ( ( a - 1.0f ) * cos ) + twoRootAlpha );
				b1 = 2.0f * a * ( ( a - 1.0f ) - ( ( a + 1.0f ) * cos ) );
				b2 = a * ( ( a + 1.0f ) - ( ( a - 1.0f ) * cos ) - twoRootAlpha );

				a0 = ( a + 1.0f ) + ( ( a - 1.0f ) * cos ) + twoRootAlpha;
				a1 = -2.0f * ( ( a - 1.0f ) + ( ( a + 1.0f ) * cos ) );
				a2 = ( a + 1.0f ) + ( ( a - 1.0f ) * cos ) - twoRootAlpha;
			}

			B0 = b0 / a0;
			B1 = b1 / a0;
			B2 = b2 / a0;
			A1 = a1 / a0;
			A2 = a2 / a0;
		}
	}

	private Biquad[] low = Array.Empty<Biquad>();
	private Biquad[] high = Array.Empty<Biquad>();

	private float lowGain = -1.0f;
	private float highGain = -1.0f;

	//Where the gain got to, so the next buffer carries on from there.
	private float gain;

	private bool started;

	//The volume is how loud it ends up - the distance and whatever gain the caller wanted, together, because they are slid towards as one. The three bands are how much of the low, the middle and the high get through.
	/// <summary>Apply a distance, an air, and whatever is in the way.</summary>
	public void Apply( float volume, float lowBand, float midBand, float highBand,
		MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( input is null || output is null ) return;

		var channels = Math.Min( input.ChannelCount, output.ChannelCount );
		if ( channels <= 0 ) return;

		Prepare( channels, lowBand, midBand, highBand );

		//The middle band is the whole signal, so it goes into the gain rather than through a filter of its own.
		var target = volume * MathF.Max( midBand, 0.0f );

		if ( !started )
		{
			gain = target;
			started = true;
		}

		var frames = input.Get( 0 ).Buffer.Length;

		//Over one buffer, not instantly: a gain that steps once a buffer is a click, and one that steps every sample is a slide.
		var step = ( target - gain ) / frames;

		for ( var channel = 0; channel < channels; channel++ )
		{
			var source = input.Get( channel ).Buffer;
			var destination = output.Get( channel ).Buffer;

			var running = gain;

			ref var lowFilter = ref low[channel];
			ref var highFilter = ref high[channel];

			for ( var frame = 0; frame < frames; frame++ )
			{
				var sample = source[frame];

				sample = lowFilter.Process( sample );
				sample = highFilter.Process( sample );

				destination[frame] = sample * running;

				running += step;
			}
		}

		gain = target;
	}

	//Make sure there is a filter per channel, set to the bands asked for. Coefficients are worked out only
	//when the bands move, because they move when a sound does and that is a great deal slower than a buffer.
	private void Prepare( int channels, float lowBand, float midBand, float highBand )
	{
		if ( low.Length < channels )
		{
			Array.Resize( ref low, channels );
			Array.Resize( ref high, channels );

			lowGain = -1.0f;
			highGain = -1.0f;
		}

		var middle = MathF.Max( midBand, 0.0001f );

		//Relative to the middle, which is what the overall gain already carries.
		var wantedLow = Math.Clamp( lowBand / middle, 0.0001f, 16.0f );
		var wantedHigh = Math.Clamp( highBand / middle, 0.0001f, 16.0f );

		if ( MathF.Abs( wantedLow - lowGain ) < 0.001f && MathF.Abs( wantedHigh - highGain ) < 0.001f ) return;

		lowGain = wantedLow;
		highGain = wantedHigh;

		for ( var channel = 0; channel < channels; channel++ )
		{
			low[channel].SetShelf( LowCrossover, wantedLow, AudioEngine.SamplingRate, high: false );
			high[channel].SetShelf( HighCrossover, wantedHigh, AudioEngine.SamplingRate, high: true );
		}
	}

	/// <summary>Forget where the sound was, for one that is starting again.</summary>
	public void Reset()
	{
		for ( var channel = 0; channel < low.Length; channel++ )
		{
			low[channel].Reset();
			high[channel].Reset();
		}

		started = false;
	}
}
