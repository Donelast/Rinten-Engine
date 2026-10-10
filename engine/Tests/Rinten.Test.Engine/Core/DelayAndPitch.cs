using System;
using Rinten.Audio;

namespace EngineTests;

//The two processors a mixer can put on a sound: repeats, and a sound moved up or down without being made shorter or longer.
[TestClass]
public class DelayAndPitchTest
{
	private const int Frames = 512;

	//Run something through an effect and hand back what came out.
	private static float[] Run( int buffers, Func<int, float> sound, Action<MultiChannelBuffer, MultiChannelBuffer> apply )
	{
		using var input = new MultiChannelBuffer( 1 );
		using var output = new MultiChannelBuffer( 1 );

		var all = new float[buffers * Frames];

		for ( var buffer = 0; buffer < buffers; buffer++ )
		{
			var samples = input.Get( 0 ).Buffer;

			for ( var frame = 0; frame < Frames; frame++ ) samples[frame] = sound( ( buffer * Frames ) + frame );

			apply( input, output );

			output.Get( 0 ).Buffer.CopyTo( all.AsSpan( buffer * Frames, Frames ) );
		}

		return all;
	}

	//A click comes back later, and quieter, and again after that.
	[TestMethod]
	public void DelayRepeatsWhatItWasGiven()
	{
		var effect = new DelayEffect();

		//A twentieth of a second, so two repeats fit in what is captured.
		var delay = 0.05f;
		var samples = (int)( delay * AudioEngine.SamplingRate );

		var output = Run( 16, frame => frame == 0 ? 1.0f : 0.0f,
			( input, result ) => effect.Apply( delay, 0.5f, input, result ) );

		//The sound itself, then its repeat, then the repeat of that.
		Assert.AreEqual( 1.0f, output[0], 0.01f, "what went in comes straight out" );

		var first = Near( output, samples );
		var second = Near( output, samples * 2 );

		Assert.IsTrue( first > 0.3f, $"it should come back after the delay - {first:0.000}" );
		Assert.IsTrue( second > 0.1f && second < first, $"and again, quieter - {second:0.000} after {first:0.000}" );
	}

	//The loudest sample anywhere near a place.
	private static float Near( float[] samples, int at )
	{
		var loudest = 0.0f;

		for ( var frame = Math.Max( at - 4, 0 ); frame < Math.Min( at + 4, samples.Length ); frame++ )
		{
			loudest = MathF.Max( loudest, MathF.Abs( samples[frame] ) );
		}

		return loudest;
	}

	//Turned all the way down, a delay is what went in.
	[TestMethod]
	public void DelayWithNoRepeatsIsJustTheSound()
	{
		var effect = new DelayEffect();

		var output = Run( 2, frame => MathF.Sin( frame * 0.05f ),
			( input, result ) => effect.Apply( 0.5f, 0.0f, input, result ) );

		//Half a second of delay and nothing fed back: the first buffers are the sound on its own, because the repeat has not arrived yet.
		for ( var frame = 0; frame < 256; frame++ )
		{
			Assert.AreEqual( MathF.Sin( frame * 0.05f ), output[frame], 0.001f );
		}
	}

	//A tone moved up is a higher tone - and still the same length, which is the whole reason this is not just playing it faster.
	[TestMethod]
	public void PitchMovesTheToneAndKeepsTheLength()
	{
		var effect = new PitchEffect();

		var rate = AudioEngine.SamplingRate;
		var tone = 440.0f;

		var output = Run( 8, frame => MathF.Sin( 2.0f * MathF.PI * tone * frame / rate ),
			( input, result ) => effect.Apply( 2.0f, input, result ) );

		//Counted rather than transformed: how many times the last buffer crosses zero says what it is, and an octave up is twice as often.
		var crossings = Crossings( output, output.Length - Frames, Frames );

		var expected = 2.0f * tone * Frames / rate * 2.0f;

		Assert.AreEqual( expected, crossings, expected * 0.25f,
			$"an octave up should cross zero about {expected:0} times, not {crossings}" );

		//And it is still playing at the end, which is what keeping the length means - a sampler played twice as fast would have run out.
		var loudest = 0.0f;

		for ( var frame = output.Length - Frames; frame < output.Length; frame++ )
		{
			loudest = MathF.Max( loudest, MathF.Abs( output[frame] ) );
		}

		Assert.IsTrue( loudest > 0.3f, $"it should still be sounding - {loudest:0.000}" );
	}

	[TestMethod]
	public void PitchOfOneIsTheSoundUntouched()
	{
		var effect = new PitchEffect();

		var output = Run( 4, frame => MathF.Sin( frame * 0.05f ),
			( input, result ) => effect.Apply( 1.0f, input, result ) );

		for ( var frame = 0; frame < output.Length; frame += 37 )
		{
			Assert.AreEqual( MathF.Sin( frame * 0.05f ), output[frame], 0.001f,
				"asking for no change should change nothing" );
		}
	}

	private static int Crossings( float[] samples, int from, int count )
	{
		var crossings = 0;

		for ( var frame = from + 1; frame < from + count; frame++ )
		{
			if ( samples[frame - 1] <= 0.0f && samples[frame] > 0.0f ) crossings++;
			if ( samples[frame - 1] >= 0.0f && samples[frame] < 0.0f ) crossings++;
		}

		return crossings;
	}
}
