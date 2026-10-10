using System;
using Rinten.Audio;

namespace EngineTests;

//Where a sound is, as the two ears hear it. Tested by the three cues a listener actually uses: which ear is louder, which ear hears it first, and which
//ear hears less of the top. A pan alone gives only the first, which is why a panned sound sits in the middle of the head rather than out in the world.
[TestClass]
public class HeadEffectTest
{
	private const int Frames = 512;

	//Both ears, given a sound from a direction.
	private static (float[] Left, float[] Right) Hear( Vector3 direction, float blend, Func<int, float> sound )
	{
		var effect = new HeadEffect();

		using var input = new MultiChannelBuffer( 1 );
		using var output = new MultiChannelBuffer( 2 );

		var left = new float[Frames];
		var right = new float[Frames];

		//A few buffers, so the gains have arrived where they were going.
		for ( var buffer = 0; buffer < 4; buffer++ )
		{
			var samples = input.Get( 0 ).Buffer;

			for ( var frame = 0; frame < Frames; frame++ ) samples[frame] = sound( ( buffer * Frames ) + frame );

			effect.Apply( direction, blend, input, output );

			output.Get( 0 ).Buffer.CopyTo( left );
			output.Get( 1 ).Buffer.CopyTo( right );
		}

		return (left, right);
	}

	//Which sample is the loudest, near the start of the buffer.
	private static int Peak( float[] samples )
	{
		var best = 0;
		var loudest = 0.0f;

		for ( var frame = 0; frame < 128; frame++ )
		{
			var level = MathF.Abs( samples[frame] );
			if ( level <= loudest ) continue;

			loudest = level;
			best = frame;
		}

		return best;
	}

	private static float Loudness( float[] samples )
	{
		var total = 0.0f;
		foreach ( var sample in samples ) total += sample * sample;

		return MathF.Sqrt( total / samples.Length );
	}

	[TestMethod]
	public void TheNearEarIsLouder()
	{
		var (left, right) = Hear( Vector3.Left, 1.0f, frame => MathF.Sin( frame * 0.05f ) );

		Assert.IsTrue( Loudness( left ) > Loudness( right ) * 1.5f,
			$"a sound on the left should be louder there - {Loudness( left ):0.000} against {Loudness( right ):0.000}" );

		var (farLeft, farRight) = Hear( Vector3.Left * -1.0f, 1.0f, frame => MathF.Sin( frame * 0.05f ) );

		Assert.IsTrue( Loudness( farRight ) > Loudness( farLeft ) * 1.5f, "and the other way round on the other side" );
	}

	//The far ear hears it later - which is the cue a listener trusts most.
	[TestMethod]
	public void TheFarEarHearsItLater()
	{
		//A click rather than a tone: a delay in a tone is a phase nobody can point at, and a delay in a click is a delay.
		var (left, right) = Hear( Vector3.Left, 1.0f, frame => frame % 256 == 0 ? 1.0f : 0.0f );

		//Where each ear is loudest, rather than where it first crosses a line: the far ear hears the click through
		//the head, which spreads it out and quietens it, and a line put anywhere is a line one of them misses.
		var leftPeak = Peak( left );
		var rightPeak = Peak( right );

		Assert.IsTrue( rightPeak > leftPeak,
			$"the far ear should hear it later - left at {leftPeak}, right at {rightPeak}" );

		//And not by more than a head is wide.
		Assert.IsTrue( rightPeak - leftPeak <= 32,
			$"but not by more than a head - {rightPeak - leftPeak} samples" );
	}

	//And through the head, which takes the top off it.
	[TestMethod]
	public void TheFarEarHearsLessOfTheTop()
	{
		//A high tone, where the head has most to take.
		var high = 9000.0f;
		var rate = AudioEngine.SamplingRate;

		var (left, right) = Hear( Vector3.Left, 1.0f, frame => MathF.Sin( 2.0f * MathF.PI * high * frame / rate ) );

		var near = Loudness( left );
		var far = Loudness( right );

		//Louder is expected; the point here is that it is much quieter than the loudness difference alone would make it.
		Assert.IsTrue( far < near * 0.4f,
			$"the head should take the top off the far ear - {far:0.000} against {near:0.000}" );
	}

	[TestMethod]
	public void SomethingNowhereComesOutOfBothEars()
	{
		var (left, right) = Hear( Vector3.Left, 0.0f, frame => MathF.Sin( frame * 0.05f ) );

		Assert.AreEqual( Loudness( left ), Loudness( right ), 0.001f,
			"a sound that is not anywhere is not on one side of the head" );
	}
}
