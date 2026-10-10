using System;
using Rinten.Audio;

namespace EngineTests;

//Baked sound wavs are hand-written (no encoder); each form must decode back to its samples
[TestClass]
public class SoundBakeTest
{
	private static bool Ready()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return false;
		}

		return true;
	}

	//A tone on the left and a different one on the right, so a swapped channel shows.
	private static float[][] Tones( int frames, int rate )
	{
		var left = new float[frames];
		var right = new float[frames];

		for ( var i = 0; i < frames; i++ )
		{
			left[i] = 0.5f * MathF.Sin( 2 * MathF.PI * 440 * i / rate );
			right[i] = 0.25f * MathF.Sin( 2 * MathF.PI * 220 * i / rate );
		}

		return [left, right];
	}

	//Everything the decoder makes of some bytes, at their own rate, a channel at a time.
	private static unsafe float[][] Decode( byte[] bytes, out CoreSound sound )
	{
		sound = CoreSound.FromMemory( bytes );
		Assert.IsNotNull( sound, "the decoder did not take the wav" );

		var sampler = new CoreAudioSampler( sound, sound.SampleRate, looping: false );
		Assert.IsTrue( sampler.IsValid );

		try
		{
			var count = sampler.ChannelCount;
			var result = new System.Collections.Generic.List<float>[count];
			for ( var c = 0; c < count; c++ ) result[c] = new();

			var buffers = new float[count][];
			for ( var c = 0; c < count; c++ ) buffers[c] = GC.AllocateArray<float>( 1024, pinned: true );

			var pointers = stackalloc float*[count];
			for ( var c = 0; c < count; c++ )
				pointers[c] = (float*)System.Runtime.CompilerServices.Unsafe.AsPointer( ref buffers[c][0] );

			while ( true )
			{
				var read = sampler.Read( pointers, count, 1024, 1.0f );
				if ( read <= 0 ) break;

				for ( var c = 0; c < count; c++ ) result[c].AddRange( buffers[c].AsSpan( 0, read ).ToArray() );

				if ( sampler.Finished ) break;
			}

			var channels = new float[count][];
			for ( var c = 0; c < count; c++ ) channels[c] = result[c].ToArray();

			return channels;
		}
		finally
		{
			sampler.Dispose();
		}
	}

	//The frame the sampler hands out late by: its resampler holds one back, even at the file's own rate, so what
	//was written as frame n is read out as frame n + 1. SoundBake takes it off again - see SoundBake.Decode.
	private const int SamplerDelay = 1;

	private static float WorstError( float[] expected, float[] actual, int frames )
	{
		var worst = 0.0f;

		for ( var i = 0; i < frames - SamplerDelay; i++ ) worst = MathF.Max( worst, MathF.Abs( expected[i] - actual[i + SamplerDelay] ) );

		return worst;
	}

	[TestMethod]
	public void Pcm16_ReadsBackAsWritten()
	{
		if ( !Ready() ) return;

		const int rate = 22050;
		const int frames = 5000;

		var tones = Tones( frames, rate );
		var decoded = Decode( Editor.WavWriter.Pcm16( tones, rate, null ), out var sound );

		Assert.AreEqual( 2, sound.ChannelCount );
		Assert.AreEqual( rate, sound.SampleRate );
		Assert.AreEqual( frames, decoded[0].Length );
		Assert.AreEqual( -1, sound.DeclaredLoopStart, "no loop was asked for" );

		//One step of 16 bits, and a little for the rounding either way.
		Assert.IsTrue( WorstError( tones[0], decoded[0], frames ) < 0.0002f );
		Assert.IsTrue( WorstError( tones[1], decoded[1], frames ) < 0.0002f );
	}

	[TestMethod]
	public void ImaAdpcm_ReadsBackCloseToWhatWasWritten()
	{
		if ( !Ready() ) return;

		const int rate = 44100;

		//Not a whole number of blocks: the last one is padded, and the fact chunk is what says where the sound really ends.
		const int frames = 3000;

		var tones = Tones( frames, rate );
		var bytes = Editor.WavWriter.ImaAdpcm( tones, rate, null );
		var decoded = Decode( bytes, out var sound );

		Assert.AreEqual( 2, sound.ChannelCount );
		Assert.AreEqual( rate, sound.SampleRate );
		Assert.IsTrue( decoded[0].Length >= frames, $"{decoded[0].Length} frames came back of {frames}" );

		//Four bits a sample: close, not exact. A channel read as the other, or nibbles in the wrong order, is off by far more than this.
		Assert.IsTrue( WorstError( tones[0], decoded[0], frames ) < 0.05f, $"left off by {WorstError( tones[0], decoded[0], frames )}" );
		Assert.IsTrue( WorstError( tones[1], decoded[1], frames ) < 0.05f, $"right off by {WorstError( tones[1], decoded[1], frames )}" );

		//And a quarter of the size, give or take the headers.
		Assert.IsTrue( bytes.Length < ( frames * 2 * 2 ) / 3, $"{bytes.Length} bytes is not compressed" );
	}

	[TestMethod]
	public void Loop_IsTheOneTheLoaderReads()
	{
		if ( !Ready() ) return;

		const int rate = 22050;
		const int frames = 4000;

		var tones = Tones( frames, rate );

		foreach ( var bytes in new[] { Editor.WavWriter.Pcm16( tones, rate, (1000, 3000) ), Editor.WavWriter.ImaAdpcm( tones, rate, (1000, 3000) ) } )
		{
			var sound = CoreSound.FromMemory( bytes );

			Assert.IsNotNull( sound );
			Assert.AreEqual( 1000, sound.DeclaredLoopStart );
			Assert.AreEqual( 3000, sound.DeclaredLoopEnd, "the end is one past the last frame played" );
		}
	}

	//A source with silence either side of a stereo tone: what trimming takes away, and a start for the loop to be counted from.
	private static CoreSound Padded( int rate, int silence, int tone, out float[][] tones )
	{
		tones = Tones( tone, rate );

		var frames = silence + tone + silence;
		var left = new float[frames];
		var right = new float[frames];

		tones[0].CopyTo( left, silence );
		tones[1].CopyTo( right, silence );

		return CoreSound.FromMemory( Editor.WavWriter.Pcm16( [left, right], rate, null ) );
	}

	[TestMethod]
	public void Bake_TrimsDownmixesTurnsDownAndLoopsFromTheTrimmedStart()
	{
		if ( !Ready() ) return;

		const int rate = 22050;
		const int silence = 500;
		const int tone = 2000;

		var source = Padded( rate, silence, tone, out var tones );
		Assert.IsNotNull( source );

		//A loop from a second part of the way into the tone, to the end: in the source's seconds, which the trim moves.
		var loopAt = silence + 1000;

		var settings = new Editor.SoundBake.Settings(
			Loop: true, LoopStart: loopAt / (float)rate, LoopEnd: 0,
			ForceMono: true, TrimSilence: true, Normalize: false, Gain: -6.0206f,
			Rate: 0, Compress: false );

		var bytes = Editor.SoundBake.Bake( source, settings );
		Assert.IsNotNull( bytes );

		var decoded = Decode( bytes, out var baked );

		Assert.AreEqual( 1, baked.ChannelCount, "forced to mono" );

		//The tone starts on a zero crossing, which is silence as far as the trim can tell - so it may take the tone's first sample with it.
		Assert.IsTrue( Math.Abs( baked.Frames - tone ) <= 2, $"{baked.Frames} frames, the tone was {tone}" );

		var lost = tone - (int)baked.Frames;

		Assert.IsTrue( Math.Abs( baked.DeclaredLoopStart - ( 1000 - lost ) ) <= 1, $"loop starts at {baked.DeclaredLoopStart}" );
		Assert.AreEqual( baked.Frames, baked.DeclaredLoopEnd, "a loop with no end runs to the end" );

		//Half as loud, and the average of the two sides.
		var expected = new float[tone - lost];
		for ( var i = 0; i < expected.Length; i++ ) expected[i] = 0.5f * ( tones[0][i + lost] + tones[1][i + lost] ) * 0.5f;

		Assert.IsTrue( WorstError( expected, decoded[0], expected.Length ) < 0.001f,
			$"off by {WorstError( expected, decoded[0], expected.Length )}" );
	}

	[TestMethod]
	public void Bake_ResamplesToTheRateAskedFor()
	{
		if ( !Ready() ) return;

		var source = Padded( 44100, 0, 4410, out _ );

		var bytes = Editor.SoundBake.Bake( source, new Editor.SoundBake.Settings( Rate: 22050 ) );
		var baked = CoreSound.FromMemory( bytes );

		Assert.AreEqual( 22050, baked.SampleRate );
		Assert.IsTrue( Math.Abs( baked.Frames - 2205 ) <= 2, $"{baked.Frames} frames for a tenth of a second" );
	}

	[TestMethod]
	public void Settings_LeftAloneChangeNothing()
	{
		Assert.IsFalse( new Editor.SoundBake.Settings().ChangesAnything( 44100 ) );
		Assert.IsFalse( new Editor.SoundBake.Settings( Rate: 44100 ).ChangesAnything( 44100 ), "the rate it already is" );
		Assert.IsTrue( new Editor.SoundBake.Settings( Rate: 22050 ).ChangesAnything( 44100 ) );
		Assert.IsTrue( new Editor.SoundBake.Settings( Gain: 3 ).ChangesAnything( 44100 ) );
	}
}
