using System;
using Rinten.Audio;

namespace EngineTests;

//Distance makes a sound quieter and duller; measured on output tones, not filter coefficients
[TestClass]
public class DirectEffectTest
{
	private const int Frames = 512;

	//Fill a buffer with a tone, and say how loud what comes back is.
	private static float Through( DirectEffect effect, float frequency, float volume,
		float low, float mid, float high, int buffers = 8 )
	{
		using var input = new MultiChannelBuffer( 1 );
		using var output = new MultiChannelBuffer( 1 );

		var rate = AudioEngine.SamplingRate;
		var phase = 0.0f;
		var loudest = 0.0f;

		for ( var buffer = 0; buffer < buffers; buffer++ )
		{
			var samples = input.Get( 0 ).Buffer;

			for ( var frame = 0; frame < Frames; frame++ )
			{
				samples[frame] = MathF.Sin( phase );
				phase += 2.0f * MathF.PI * frequency / rate;
			}

			effect.Apply( volume, low, mid, high, input, output );

			//The last buffer only: the first few are the filters settling and the gain sliding to
			//where it was asked for, which is the point of them but not what is being measured here.
			if ( buffer < buffers - 1 ) continue;

			var result = output.Get( 0 ).Buffer;

			for ( var frame = 0; frame < Frames; frame++ )
			{
				loudest = MathF.Max( loudest, MathF.Abs( result[frame] ) );
			}
		}

		return loudest;
	}

	[TestMethod]
	public void VolumeIsWhatComesOut()
	{
		var effect = new DirectEffect();

		Assert.AreEqual( 1.0f, Through( effect, 440.0f, 1.0f, 1.0f, 1.0f, 1.0f ), 0.02f,
			"nothing in the way and full volume is the sound as it was" );

		effect.Reset();

		Assert.AreEqual( 0.5f, Through( effect, 440.0f, 0.5f, 1.0f, 1.0f, 1.0f ), 0.02f,
			"half the volume is half as loud" );

		effect.Reset();

		Assert.AreEqual( 0.0f, Through( effect, 440.0f, 0.0f, 1.0f, 1.0f, 1.0f ), 0.01f,
			"and none of it is silence" );
	}

	//Air takes the high frequencies first, which is what makes something far away sound far away rather than just quiet.
	[TestMethod]
	public void HighFrequenciesGoFirst()
	{
		var effect = new DirectEffect();

		//What a long way off through air looks like: the top is halved, the bottom is barely touched.
		var lowTone = Through( effect, 120.0f, 1.0f, 1.0f, 0.8f, 0.4f );

		effect.Reset();

		var highTone = Through( effect, 8000.0f, 1.0f, 1.0f, 0.8f, 0.4f );

		Assert.IsTrue( highTone < lowTone * 0.75f,
			$"a high tone should lose more than a low one - low {lowTone:0.000}, high {highTone:0.000}" );

		Assert.IsTrue( lowTone > 0.6f, $"and the low one should still be there - {lowTone:0.000}" );
	}

	//A wall does the same thing harder: what gets through is what the bands say gets through.
	[TestMethod]
	public void OcclusionTakesTheBandsItIsGiven()
	{
		var effect = new DirectEffect();

		var open = Through( effect, 1000.0f, 1.0f, 1.0f, 1.0f, 1.0f );

		effect.Reset();

		var behindAWall = Through( effect, 1000.0f, 1.0f, 0.5f, 0.2f, 0.05f );

		Assert.IsTrue( behindAWall < open * 0.4f,
			$"a fifth of the middle through should be much quieter - {behindAWall:0.000} against {open:0.000}" );
	}

	//A sound that jumps from silent to loud slides rather than steps: a gain applied a
	//buffer at a time is a click, and a click is the one thing a mixer must never make.
	[TestMethod]
	public void GainSlidesRatherThanSteps()
	{
		var effect = new DirectEffect();

		using var input = new MultiChannelBuffer( 1 );
		using var output = new MultiChannelBuffer( 1 );

		//A constant, so anything that moves in the output is the gain moving.
		input.Get( 0 ).Buffer.Fill( 1.0f );

		//Quiet first, so there is something to slide from.
		effect.Apply( 0.0f, 1.0f, 1.0f, 1.0f, input, output );

		effect.Apply( 1.0f, 1.0f, 1.0f, 1.0f, input, output );

		var samples = output.Get( 0 ).Buffer;

		Assert.IsTrue( samples[0] < 0.1f, $"it should start where it left off - {samples[0]:0.000}" );
		Assert.IsTrue( samples[Frames - 1] > 0.9f, $"and arrive by the end - {samples[Frames - 1]:0.000}" );

		//No two neighbours far apart: that is what a click is.
		var biggest = 0.0f;

		for ( var frame = 1; frame < Frames; frame++ )
		{
			biggest = MathF.Max( biggest, MathF.Abs( samples[frame] - samples[frame - 1] ) );
		}

		Assert.IsTrue( biggest < 0.01f, $"the biggest step between samples was {biggest:0.0000}" );
	}
}
