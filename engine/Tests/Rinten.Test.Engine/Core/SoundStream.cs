using System;
using Rinten.Audio;

namespace EngineTests;

//Sound the game is making up as it goes: a voice off the network, a video's soundtrack, anything generated.
[TestClass]
public class StreamBufferTest
{
	[TestMethod]
	public void WhatIsWrittenIsWhatIsRead()
	{
		var stream = new StreamBuffer( (int)AudioEngine.SamplingRate, 1 );

		using var buffer = new MultiChannelBuffer( 1 );

		var written = new short[512];
		for ( var i = 0; i < written.Length; i++ ) written[i] = (short)( i * 32 );

		Assert.AreEqual( 512, stream.Write( written ), "everything should have fitted" );
		Assert.AreEqual( 512, stream.Queued );

		var read = stream.Read( buffer, 512, (int)AudioEngine.SamplingRate );

		//One short of the buffer: reading between two samples needs the one after, and the last one has nothing after it yet.
		Assert.IsTrue( read >= 510, $"nearly all of it should have come back - {read}" );

		var samples = buffer.Get( 0 ).Buffer;

		for ( var frame = 0; frame < 500; frame += 64 )
		{
			Assert.AreEqual( written[frame] / 32768.0f, samples[frame], 0.001f, $"frame {frame}" );
		}
	}

	[TestMethod]
	public void ARingThatRunsDryIsSilentRatherThanStale()
	{
		var stream = new StreamBuffer( (int)AudioEngine.SamplingRate, 1 );

		using var buffer = new MultiChannelBuffer( 1 );

		var written = new short[64];
		for ( var i = 0; i < written.Length; i++ ) written[i] = short.MaxValue / 2;

		stream.Write( written );

		var read = stream.Read( buffer, 512, (int)AudioEngine.SamplingRate );

		Assert.IsTrue( read < 512, "there was less in it than was asked for" );

		var samples = buffer.Get( 0 ).Buffer;

		Assert.AreEqual( 0.0f, samples[511], 0.0001f, "and the rest is silence" );
	}

	//A voice arrives at its own rate, and the mixer works at another.
	[TestMethod]
	public void AStreamIsResampledToTheMixersRate()
	{
		//Half the mixer's rate, which is what voice usually is.
		var rate = (int)AudioEngine.SamplingRate / 2;

		var stream = new StreamBuffer( rate, 1 );

		using var buffer = new MultiChannelBuffer( 1 );

		//A second of ramp at the stream's rate.
		var written = new short[1024];
		for ( var i = 0; i < written.Length; i++ ) written[i] = (short)( i * 16 );

		stream.Write( written );

		var read = stream.Read( buffer, 512, (int)AudioEngine.SamplingRate );

		Assert.AreEqual( 512, read, "twice the rate is twice as many frames out" );

		var samples = buffer.Get( 0 ).Buffer;

		//Frame 400 out is frame 200 in.
		Assert.AreEqual( written[200] / 32768.0f, samples[400], 0.002f );

		Assert.IsTrue( stream.Queued < 800, "and it should have eaten about half of what it had" );
	}

	[TestMethod]
	public void AClosedStreamPlaysOutAndStops()
	{
		var stream = new StreamBuffer( (int)AudioEngine.SamplingRate, 1 );
		var sampler = new StreamSampler( stream );

		try
		{
			Assert.IsFalse( sampler.IsReadyToMix, "nothing has been written yet" );

			stream.Write( new short[1024] );

			Assert.IsTrue( sampler.IsReadyToMix );
			Assert.IsTrue( sampler.ShouldContinueMixing );

			stream.Close();

			//Still going while there is anything left in it.
			Assert.IsTrue( sampler.ShouldContinueMixing, "there is still sound to play" );

			sampler.Sample( 1.0f );
			sampler.Sample( 1.0f );
			sampler.Sample( 1.0f );

			Assert.IsFalse( sampler.ShouldContinueMixing, "and then it is done" );
		}
		finally
		{
			sampler.Dispose();
		}
	}
}
