
namespace Rinten;

/// <summary>Sound the game makes up as it goes: samples written in, played out as they arrive. A voice off the network, speech being synthesised, anything generated.</summary>
public sealed partial class SoundStream : IHandle, IDisposable
{
	#region IHandle
	void IHandle.HandleInit( IntPtr ptr )
	{
	}

	void IHandle.HandleDestroy()
	{
	}

	bool IHandle.HandleValid() => ours is not null;

	#endregion

	/// <summary>Number of samples per second, as set during its creation.</summary>
	public int SampleRate { get; internal set; }

	/// <summary>Number of audio channels, as set during its creation.</summary>
	public int Channels { get; internal set; }

	//A ring the game writes into and the mixer reads out of. Null where there is
	//nothing to play through - a server, a test - and every write is then dropped.
	private Audio.StreamBuffer ours;

	/// <summary>How many samples - across every channel - are written and not yet played.</summary>
	public int QueuedSampleCount => ours is null ? 0 : ours.Queued * Channels;

	/// <summary>How many more samples - across every channel - fit before a write starts dropping them.</summary>
	public int MaxWriteSampleCount => ours is null ? 0 : Math.Max( ours.Free, 0 ) * Channels;

	/// <summary>How long a sample written now waits before it is heard, in samples of this stream: what is queued ahead of it.</summary>
	public int LatencySamplesCount => ours is null ? 0 : ours.Queued;

	internal SoundStream() { }
	internal SoundStream( HandleCreationData _ ) { }

	public SoundStream( int sampleRate = 44100, int channels = 1 ) : this()
	{
		if ( channels <= 0 || channels > 16 )
			throw new ArgumentException( "Invalid number of channels" );

		if ( sampleRate < 1024 )
			throw new ArgumentException( "Invalid sample rate" );

		Channels = channels;
		SampleRate = sampleRate;

		if ( Application.IsHeadless || Application.IsUnitTest )
			return;

		ours = new Audio.StreamBuffer( sampleRate, channels );
	}

	/// <summary>Adds samples, interleaved when there is more than one channel. What does not fit is dropped rather than waited for.</summary>
	public void WriteData( Span<short> data )
	{
		if ( ours is not null && data.Length > 0 ) ours.Write( data );
	}

	/// <summary>Close the stream: signals that no more data will be written. Once the internal buffer drains, <see cref="SoundHandle.IsPlaying"/> will become <c>false</c>.</summary>
	public void Close() => ours?.Close();

	public void Dispose()
	{
		//Closed rather than thrown away: whatever is playing it reads what is left and finishes, which is what closing a stream means.
		ours?.Close();
		ours = null;
	}

	/// <summary>Play sound of the stream.</summary>
	public SoundHandle Play( float volume = 1.0f, float pitch = 1.0f )
	{
		if ( ours is null ) return default;

		return Sound.PlayStream( new Audio.StreamSampler( ours ), SampleRate, volume, pitch, "Sound Stream" );
	}

	/// <summary>Play sound of the stream.</summary>
	[Obsolete( "Decibels are obsolete" )]
	public SoundHandle Play( float volume, float pitch, float decibels ) => Play( volume, pitch );
}
