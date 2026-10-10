using System;
using System.IO;
using Rinten.Audio;

namespace EngineTests;

//Sounds, and the samplers that read them. Everything here is measured off a file this test wrote itself, so what comes
//back out can be compared with what went in sample by sample. A decoder that is a semitone out sounds fine and is wrong.
[TestClass]
public class SoundTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-sound-{Guid.NewGuid():N}{extension}" );

	//A wav of 16-bit samples, written by hand. By hand because the point is to know exactly what
	//is in it: the samples are a ramp, so where the playhead is can be read off any one of them.
	private static void WriteWav( string path, int rate, int channels, int frames, Func<int, int, float> sample )
	{
		using var file = new BinaryWriter( File.Create( path ) );

		var bytesPerSample = 2;
		var data = frames * channels * bytesPerSample;

		file.Write( "RIFF"u8 );
		file.Write( 36 + data );
		file.Write( "WAVE"u8 );

		file.Write( "fmt "u8 );
		file.Write( 16 );                                        //chunk size
		file.Write( (short)1 );                                  //PCM
		file.Write( (short)channels );
		file.Write( rate );
		file.Write( rate * channels * bytesPerSample );          //bytes a second
		file.Write( (short)( channels * bytesPerSample ) );      //bytes a frame
		file.Write( (short)( bytesPerSample * 8 ) );

		file.Write( "data"u8 );
		file.Write( data );

		for ( var frame = 0; frame < frames; frame++ )
		{
			for ( var channel = 0; channel < channels; channel++ )
			{
				var value = Math.Clamp( sample( frame, channel ), -1.0f, 1.0f );
				file.Write( (short)( value * short.MaxValue ) );
			}
		}
	}

	private static bool Ready()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return false;
		}

		return true;
	}

	//Read frames out of a sampler, a channel at a time.
	private static unsafe int Read( CoreAudioSampler sampler, float[][] channels, int frames, float pitch = 1.0f )
	{
		var pointers = stackalloc float*[channels.Length];
		var handles = new System.Runtime.InteropServices.GCHandle[channels.Length];

		try
		{
			for ( var i = 0; i < channels.Length; i++ )
			{
				handles[i] = System.Runtime.InteropServices.GCHandle.Alloc( channels[i],
					System.Runtime.InteropServices.GCHandleType.Pinned );

				pointers[i] = (float*)handles[i].AddrOfPinnedObject();
			}

			return sampler.Read( pointers, channels.Length, frames, pitch );
		}
		finally
		{
			foreach ( var handle in handles )
			{
				if ( handle.IsAllocated ) handle.Free();
			}
		}
	}

	[TestMethod]
	public void SoundKnowsWhatIsInTheFile()
	{
		if ( !Ready() ) return;

		var path = TempPath( ".wav" );

		//Two seconds of stereo at 22050, which is neither the mixer's rate nor its channel count - a sound is whatever the artist exported.
		WriteWav( path, 22050, 2, 44100, ( frame, channel ) => 0.0f );

		try
		{
			var sound = CoreSound.Load( path );
			Assert.IsNotNull( sound, "a wav we just wrote should load" );

			Assert.AreEqual( 2, sound.ChannelCount );
			Assert.AreEqual( 22050, sound.SampleRate );
			Assert.AreEqual( 44100L, sound.Frames );
			Assert.AreEqual( 2.0f, sound.Duration, 0.01f );

			Assert.IsNull( CoreSound.Load( path + ".nothing" ), "a file that is not there is not a sound" );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//What is read back is what was written - and each channel is its own, which is how a mixer holds sound.
	[TestMethod]
	public void SamplerReadsTheSamplesThatWereWritten()
	{
		if ( !Ready() ) return;

		var path = TempPath( ".wav" );

		//Left is a ramp, right is the same ramp upside down: a channel that ends up in the wrong buffer is then obvious rather than plausible.
		WriteWav( path, 44100, 2, 1024, ( frame, channel ) =>
		{
			var ramp = frame / 1024.0f * 0.5f;
			return channel == 0 ? ramp : -ramp;
		} );

		try
		{
			var sound = CoreSound.Load( path );
			Assert.IsNotNull( sound );

			var sampler = new CoreAudioSampler( sound, 44100, false );
			Assert.IsTrue( sampler.IsValid, "a sound should be playable" );

			try
			{
				Assert.AreEqual( 2, sampler.ChannelCount );

				var channels = new[] { new float[512], new float[512] };

				var read = Read( sampler, channels, 512 );
				Assert.AreEqual( 512, read, "the sound is longer than what was asked for" );

				//Rates match, so nothing was resampled and the samples are the ones that were written, give or take sixteen bits of rounding.
				for ( var frame = 0; frame < 512; frame += 64 )
				{
					var expected = frame / 1024.0f * 0.5f;

					Assert.AreEqual( expected, channels[0][frame], 0.001f, $"left, frame {frame}" );
					Assert.AreEqual( -expected, channels[1][frame], 0.001f, $"right, frame {frame}" );
				}

				Assert.AreEqual( 512L, sampler.Position );
				Assert.IsFalse( sampler.Finished );

				//The rest of it, and then the end: what is past the end is silence rather than whatever was in the buffer.
				Read( sampler, channels, 512 );

				var last = Read( sampler, channels, 512 );

				Assert.AreEqual( 0, last, "there was nothing left to read" );
				Assert.IsTrue( sampler.Finished );
				Assert.AreEqual( 0.0f, channels[0][0], 0.0001f, "past the end is silence" );
			}
			finally
			{
				sampler.Dispose();
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//Pitch is the rate it is read at: twice the pitch is half the time.
	[TestMethod]
	public void PitchReadsTheSoundFaster()
	{
		if ( !Ready() ) return;

		var path = TempPath( ".wav" );

		WriteWav( path, 44100, 1, 4096, ( frame, channel ) => frame / 4096.0f * 0.5f );

		try
		{
			var sound = CoreSound.Load( path );
			Assert.IsNotNull( sound );

			var sampler = new CoreAudioSampler( sound, 44100, false );

			try
			{
				var channels = new[] { new float[512] };

				Read( sampler, channels, 512, 2.0f );

				//Half a second of sound in a quarter of one: the sample at the end of the buffer is the one that was twice as far in.
				var expected = ( 511 * 2 ) / 4096.0f * 0.5f;

				Assert.AreEqual( expected, channels[0][511], 0.01f,
					"at twice the pitch the playhead moves twice as fast" );
			}
			finally
			{
				sampler.Dispose();
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A sound at a rate the mixer does not work in is resampled to it - which is most sounds, and the thing that is silently wrong if it is not done.
	[TestMethod]
	public void SoundsAreResampledToTheMixersRate()
	{
		if ( !Ready() ) return;

		var path = TempPath( ".wav" );

		//Half the mixer's rate: a second of it should come out as a second.
		WriteWav( path, 22050, 1, 22050, ( frame, channel ) => frame / 22050.0f * 0.5f );

		try
		{
			var sound = CoreSound.Load( path );
			var sampler = new CoreAudioSampler( sound, 44100, false );

			try
			{
				var channels = new[] { new float[441] };

				//A hundredth of a second at the mixer's rate is a two hundredth of the way through a sound that lasts a second.
				Read( sampler, channels, 441 );

				Assert.AreEqual( 0.005f, channels[0][440], 0.002f,
					"a sound at half the rate should still play in real time" );
			}
			finally
			{
				sampler.Dispose();
			}
		}
		finally
		{
			File.Delete( path );
		}
	}

	//The managed end: a sound of ours, played through the sampler the mixer asks, filling the buffers the mixer reads.
	[TestMethod]
	public void OurSamplerFillsTheMixersBuffers()
	{
		if ( !Ready() ) return;

		var audio = TempPath( ".wav" );
		var definition = Path.ChangeExtension( audio, ".snd" );

		WriteWav( audio, 44100, 1, 4096, ( frame, channel ) => frame / 4096.0f * 0.5f );

		File.WriteAllText( definition,
			"{ \"Source\": " + System.Text.Json.JsonSerializer.Serialize( Path.GetFileName( audio ) ) +
			", \"Looping\": false }" );

		try
		{
			var sampler = Rinten.Audio.CoreSounds.Sampler( definition.Replace( '\\', '/' ) );

			Assert.IsNotNull( sampler, "a sound of ours should give a sampler" );

			//The file itself is also a sound: a game names the wav, and a compiled .vsnd beside it does not change what it asked for.
			var direct = Rinten.Audio.CoreSounds.Sampler( audio.Replace( '\\', '/' ) );

			Assert.IsNotNull( direct, "an audio file is a sound without anything else" );
			direct.Dispose();

			//And the name of the compiled sound means the file it was made from.
			var compiled = Rinten.Audio.CoreSounds.Sampler( audio.Replace( '\\', '/' ) + ".vsnd" );

			Assert.IsNotNull( compiled, "a s&box sound name means the file beside it" );
			compiled.Dispose();

			try
			{
				Assert.AreEqual( 1, sampler.ChannelCount, "the sound is mono, so the sampler is" );
				Assert.IsTrue( sampler.IsReadyToMix );
				Assert.IsTrue( sampler.ShouldContinueMixing );

				sampler.Sample( 1.0f );

				var samples = sampler.GetLastReadSamples().Get( 0 ).Buffer;

				//Straight out of the file, in the mixer's own buffer.
				Assert.AreEqual( 0.0f, samples[0], 0.002f );
				Assert.AreEqual( 256 / 4096.0f * 0.5f, samples[256], 0.002f );
				Assert.AreEqual( 511 / 4096.0f * 0.5f, samples[511], 0.002f );

				Assert.AreEqual( 512, sampler.SamplePosition, "and the playhead moved by a buffer" );

				//Played out: eight buffers is the whole of it, and after that there is nothing left to mix.
				for ( var i = 0; i < 8; i++ ) sampler.Sample( 1.0f );

				Assert.IsFalse( sampler.ShouldContinueMixing, "the sound has played out" );
			}
			finally
			{
				sampler.Dispose();
			}
		}
		finally
		{
			File.Delete( audio );
			File.Delete( definition );
		}
	}

	//Ogg, which is decoded in one go rather than as it plays. Against a real file from the project, because an ogg cannot be
	//written by hand the way a wav can - and because what is being checked is that a format nobody here wrote reads correctly.
	[TestMethod]
	public void OggIsReadToo()
	{
		if ( !Ready() ) return;

		var path = "addons/menu/Assets/music/furniture_shop_loop.ogg";

		//Looked for on disk as well as through the mounted filesystems: nothing is mounted
		//in a test host, and this file is part of the checkout rather than of any project.
		var full = CoreModel.ResolveOnDisk( path );

		if ( full is null )
		{
			var root = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

			if ( !string.IsNullOrWhiteSpace( root ) )
			{
				var beside = Path.GetFullPath( Path.Combine( root, path ) );
				if ( File.Exists( beside ) ) full = beside;
			}
		}

		if ( full is null )
		{
			Assert.Inconclusive( $"{path} is not in this checkout" );
			return;
		}

		var sound = CoreSound.Load( full );

		Assert.IsNotNull( sound, "an ogg should read" );

		{
			Assert.IsTrue( sound.ChannelCount is 1 or 2 );
			Assert.IsTrue( sound.SampleRate >= 8000 );
			Assert.IsTrue( sound.Duration > 1.0f, "and it is music, so it lasts a while" );

			var sampler = new CoreAudioSampler( sound, 44100, false );
			Assert.IsTrue( sampler.IsValid );

			try
			{
				var channels = new[] { new float[512], new float[512] };

				var read = Read( sampler, channels, 512 );
				Assert.AreEqual( 512, read );

				//Something rather than silence: an ogg that decodes to nothing loads and plays and is wrong.
				var loudest = 0.0f;
				for ( var frame = 0; frame < 512; frame++ ) loudest = MathF.Max( loudest, MathF.Abs( channels[0][frame] ) );

				Assert.IsTrue( loudest > 0.0001f, "there should be sound in it" );
			}
			finally
			{
				sampler.Dispose();
			}
		}
	}

	//A looping sound never finishes, and a seek moves the playhead.
	[TestMethod]
	public void LoopingAndSeeking()
	{
		if ( !Ready() ) return;

		var path = TempPath( ".wav" );

		WriteWav( path, 44100, 1, 1024, ( frame, channel ) => frame / 1024.0f * 0.5f );

		try
		{
			var sound = CoreSound.Load( path );
			var sampler = new CoreAudioSampler( sound, 44100, true );

			try
			{
				var channels = new[] { new float[1024] };

				//Twice round: what comes back is the sound again, not silence.
				Read( sampler, channels, 1024 );
				var read = Read( sampler, channels, 1024 );

				Assert.AreEqual( 1024, read, "a loop always has more to give" );
				Assert.IsFalse( sampler.Finished );

				Assert.AreEqual( 0.25f, channels[0][512], 0.01f, "and it is the same sound" );

				//Seeking is in the mixer's frames, not the file's.
				Assert.IsTrue( sampler.Seek( 512 ) );
				Assert.AreEqual( 512L, sampler.Position );

				Read( sampler, channels, 64 );

				//From the first sample, not the second: a resampler holds the frame before the one it is
				//playing, and after a jump it is given the right one rather than silence or the old one.
				Assert.AreEqual( 0.25f, channels[0][0], 0.01f, "which is halfway through this one" );
			}
			finally
			{
				sampler.Dispose();
			}
		}
		finally
		{
			File.Delete( path );
		}
	}
}
