namespace Rinten.Audio;

//Plays a stream the game fills as it goes; running dry gives silence rather than ending
internal sealed class StreamSampler : AudioSampler
{
	private readonly StreamBuffer stream;
	private MultiChannelBuffer buffer;

	private int position;

	public StreamSampler( StreamBuffer stream )
	{
		this.stream = stream;

		ChannelCount = Math.Clamp( stream?.Channels ?? 1, 1, 8 );

		buffer = new MultiChannelBuffer( ChannelCount );
	}

	public override int ChannelCount { get; }

	//A stream that has been written to is ready; one that has not is not, so that a sound started before its first samples arrive does not begin by playing the silence it was waiting through.
	/// <summary>Whether there is anything to play yet.</summary>
	public override bool IsReadyToMix => stream is not null && ( stream.Queued > 0 || stream.Closed );

	/// <summary>It goes on until the writer says it will write no more and what is left has been played.</summary>
	public override bool ShouldContinueMixing => stream is not null && ( !stream.Closed || stream.Queued > 0 );

	public override int SamplePosition
	{
		get => position;
		set { /* A stream has no other place to be: what has gone is gone. */ }
	}

	internal override void DelayOrSkipSamples( int numSamples )
	{
		//Nothing to skip to. A stream starts when its first samples arrive.
	}

	public override void Sample( float pitch )
	{
		if ( buffer is null ) return;

		if ( stream is null )
		{
			buffer.Silence();
			return;
		}

		//Pitch on a stream is the rate it is read at, which is the same knob the rate conversion already turns.
		var rate = (int)( AudioEngine.SamplingRate / MathF.Max( pitch, 0.01f ) );

		var read = stream.Read( buffer, AudioEngine.MixBufferSize, rate );

		position += read;

		if ( tap is { } ring ) Tap( ring, read );
	}

	//The last CoreAudioSampler.TapLength frames that went out, folded to one channel, as a ring; null
	//until somebody asks - the same tap a song has, for a video's soundtrack to be looked at the same way.
	private float[] tap;
	private int tapAt;

	//Starts keeping what goes out, and hands back the most recent of it, oldest first. Main thread; see CoreAudioSampler.Listen.
	internal bool Listen( Span<float> into )
	{
		if ( tap is null )
		{
			tap = new float[CoreAudioSampler.TapLength];
			return false;
		}

		var at = System.Threading.Volatile.Read( ref tapAt );

		for ( var i = 0; i < into.Length && i < tap.Length; ++i )
			into[i] = tap[( at + i ) % tap.Length];

		return true;
	}

	private void Tap( float[] ring, int produced )
	{
		var at = tapAt;
		var scale = 1.0f / buffer.ChannelCount;

		for ( var frame = 0; frame < produced; ++frame )
		{
			var sum = 0.0f;
			for ( var channel = 0; channel < buffer.ChannelCount; ++channel ) sum += buffer.Get( channel ).Buffer[frame];

			ring[at] = sum * scale;
			if ( ++at >= ring.Length ) at = 0;
		}

		System.Threading.Volatile.Write( ref tapAt, at );
	}

	public override MultiChannelBuffer GetLastReadSamples() => buffer;

	public override void Dispose()
	{
		if ( buffer is null ) return;

		MainThread.QueueDispose( buffer );
		buffer = null;
	}
}
