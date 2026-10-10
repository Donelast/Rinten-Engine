using System;
using Rinten.Audio;

namespace EngineTests;

//Reverb has no reference output: check it tails after the sound and longer rooms last longer
[TestClass]
public class ReverbEffectTest
{
	private const int Frames = 512;

	//Clap once, then listen: how loud the tail still is after a time.
	private static float TailAfter( float seconds, float decay )
	{
		var effect = new ReverbEffect();

		using var input = new MultiChannelBuffer( 1 );
		using var output = new MultiChannelBuffer( 1 );

		//One buffer of noise, and silence after it - which is what an impulse is, once it has been through anything at all.
		var random = new Random( 1 );
		var samples = input.Get( 0 ).Buffer;

		for ( var frame = 0; frame < Frames; frame++ ) samples[frame] = (float)( random.NextDouble() * 2.0 - 1.0 );

		effect.Apply( decay, decay, decay, input, output );

		input.Get( 0 ).Buffer.Clear();

		var buffers = (int)( seconds * AudioEngine.SamplingRate / Frames );
		var loudest = 0.0f;

		for ( var buffer = 0; buffer < buffers; buffer++ )
		{
			effect.Apply( decay, decay, decay, input, output );

			loudest = 0.0f;
			var tail = output.Get( 0 ).Buffer;

			for ( var frame = 0; frame < Frames; frame++ ) loudest = MathF.Max( loudest, MathF.Abs( tail[frame] ) );
		}

		return loudest;
	}

	[TestMethod]
	public void SomethingComesOutAfterTheSoundStops()
	{
		//A second into a two second room, there should still be something there - that is the whole of what a tail is.
		var tail = TailAfter( 1.0f, 2.0f );

		Assert.IsTrue( tail > 0.0005f, $"a room should still be sounding a second later - {tail:0.00000}" );
	}

	[TestMethod]
	public void ALongerRoomLastsLonger()
	{
		var shortRoom = TailAfter( 0.7f, 0.4f );
		var longRoom = TailAfter( 0.7f, 3.0f );

		Assert.IsTrue( longRoom > shortRoom * 4.0f,
			$"three seconds should outlast four tenths by a long way - {longRoom:0.00000} against {shortRoom:0.00000}" );
	}

	[TestMethod]
	public void ARoomEventuallyGoesQuiet()
	{
		//Well past the end of a short room: a reverb whose feedback is a fraction too high never
		//stops, and never stopping is the failure nobody notices until the mix is full of mud.
		var tail = TailAfter( 3.0f, 0.3f );

		Assert.IsTrue( tail < 0.001f, $"a short room should have died away - {tail:0.00000}" );
	}

	//Two ears should not be given the same room to the sample, or a stereo tail collapses into the middle of the head.
	[TestMethod]
	public void ChannelsAreNotIdentical()
	{
		var effect = new ReverbEffect();

		using var input = new MultiChannelBuffer( 2 );
		using var output = new MultiChannelBuffer( 2 );

		var random = new Random( 2 );

		for ( var channel = 0; channel < 2; channel++ )
		{
			var samples = input.Get( channel ).Buffer;
			for ( var frame = 0; frame < Frames; frame++ ) samples[frame] = (float)( random.NextDouble() * 2.0 - 1.0 );
		}

		effect.Apply( 1.5f, 1.5f, 1.5f, input, output );

		input.Get( 0 ).Buffer.Clear();
		input.Get( 1 ).Buffer.Clear();

		for ( var buffer = 0; buffer < 20; buffer++ ) effect.Apply( 1.5f, 1.5f, 1.5f, input, output );

		var left = output.Get( 0 ).Buffer;
		var right = output.Get( 1 ).Buffer;

		var difference = 0.0f;
		for ( var frame = 0; frame < Frames; frame++ ) difference += MathF.Abs( left[frame] - right[frame] );

		Assert.IsTrue( difference > 0.001f, "the two ears should be hearing slightly different rooms" );
	}
}
