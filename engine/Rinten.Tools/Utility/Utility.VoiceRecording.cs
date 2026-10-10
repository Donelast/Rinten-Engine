using System;
using Rinten;
using Rinten.Audio;

namespace Editor;

public static partial class EditorUtility
{
	/// <summary>Recording from the machine's microphone.</summary>
	public static class VoiceRecording
	{
		private static CoreAudioCapture capture;
		private static float[] recorded;
		private static int recordedFrames;
		private static int recordedRate = 44100;
		private static int recordedChannels = 1;
		private static bool reachedLimit;

		//How long a recording can run when nobody says otherwise.
		private const int DefaultSeconds = 300;

		/// <summary>Whether this machine can record at all - which now means whether the core is loaded, not which operating system it is.</summary>
		public static bool IsSupported => CoreNative.Available;

		/// <summary>Whether something is being recorded right now.</summary>
		public static bool IsRecording => capture is not null;

		/// <summary>Whether the last recording ran to the end of its buffer and stopped early. What was captured up to that point is still there.</summary>
		public static bool ReachedLimit => reachedLimit;

		/// <summary>Start recording from the microphone.</summary>
		public static void Start( int samples = 44100, int seconds = DefaultSeconds )
		{
			if ( !IsSupported ) return;

			Flush();

			//Mono, as the MCI version asked for: this records a voice, and a second channel of the same voice is twice the file for nothing.
			capture = CoreAudioCapture.Open( samples, 1, seconds );

			if ( capture is null )
			{
				Log.Warning( "Voice recording: the machine has no capture device." );
				return;
			}

			recordedRate = capture.SampleRate;
			recordedChannels = capture.ChannelCount;
		}

		/// <summary>Stop recording. What was captured stays until <see cref="Flush"/> or the next <see cref="Start"/>, which is what <see cref="Save"/> writes.</summary>
		public static void Stop()
		{
			if ( capture is null ) return;

			var frames = capture.Frames;

			reachedLimit = capture.Full;

			if ( frames > 0 )
			{
				recorded = new float[frames * recordedChannels];
				recordedFrames = capture.Read( recorded, frames );
			}
			else
			{
				recorded = null;
				recordedFrames = 0;
			}

			//The samples are copied out now, so the device and its buffer can go.
			capture.Dispose();
			capture = null;
		}

		/// <summary>Throw away what was recorded, and stop if it is still recording.</summary>
		public static void Flush()
		{
			if ( capture is not null )
			{
				capture.Dispose();
				capture = null;
			}

			recorded = null;
			recordedFrames = 0;
			reachedLimit = false;
		}

		/// <summary>Write what was recorded to a WAV file.</summary>
		public static bool Save( string path )
		{
			if ( string.IsNullOrWhiteSpace( path ) ) return false;

			//Recording and then saving without stopping is what a person means by save, so it does not need saying twice.
			if ( capture is not null ) Stop();

			if ( recorded is null || recordedFrames <= 0 ) return false;

			try
			{
				System.IO.File.WriteAllBytes( path, Wav( recorded, recordedFrames, recordedChannels, recordedRate ) );
				return true;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Voice recording: could not write {path}" );
				return false;
			}
		}

		//Samples as a 16-bit PCM WAV; the header is written here since SoundFile's is private
		private static byte[] Wav( float[] samples, int frames, int channels, int rate )
		{
			var count = frames * channels;
			var bytes = new byte[44 + ( count * 2 )];

			using var stream = new System.IO.MemoryStream( bytes );
			using var writer = new System.IO.BinaryWriter( stream );

			var blockAlign = channels * 2;

			writer.Write( "RIFF"u8 );
			writer.Write( 36 + ( count * 2 ) );
			writer.Write( "WAVE"u8 );
			writer.Write( "fmt "u8 );
			writer.Write( 16 );
			writer.Write( (short)1 );
			writer.Write( (short)channels );
			writer.Write( rate );
			writer.Write( rate * blockAlign );
			writer.Write( (short)blockAlign );
			writer.Write( (short)16 );
			writer.Write( "data"u8 );
			writer.Write( count * 2 );

			for ( var i = 0; i < count; i++ )
			{
				//Clamped, because a device is entitled to hand back more than one and wrapping turns a loud moment into a bang.
				var value = samples[i];
				if ( value > 1.0f ) value = 1.0f;
				if ( value < -1.0f ) value = -1.0f;

				writer.Write( (short)( value * short.MaxValue ) );
			}

			writer.Flush();

			return bytes;
		}
	}
}
