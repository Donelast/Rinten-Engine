using System.Threading;

namespace Rinten.Audio;

//Lock-free single-writer/single-reader ring of float samples at the writer's rate
internal sealed class StreamBuffer
{
	private readonly float[] samples;

	private int write;
	private int read;

	//How far past the read index the next output frame falls, as a fraction of a frame. Carried from one read to the next: starting every buffer on
	//a whole frame would drop the fraction each time, and at two rates that do not divide the stream drifts out of tune and clicks at every buffer.
	private double phase;

	public StreamBuffer( int sampleRate, int channels, float seconds = 4.0f )
	{
		SampleRate = Math.Max( sampleRate, 1 );
		Channels = Math.Clamp( channels, 1, 8 );

		samples = new float[(int)( SampleRate * Channels * seconds )];
	}

	public int SampleRate { get; }

	public int Channels { get; }

	/// <summary>Whether the writer has said there will be no more.</summary>
	public bool Closed { get; private set; }

	public void Close() => Closed = true;

	/// <summary>How many frames are waiting to be played.</summary>
	public int Queued
	{
		get
		{
			var used = Volatile.Read( ref write ) - Volatile.Read( ref read );
			if ( used < 0 ) used += samples.Length;

			return used / Channels;
		}
	}

	/// <summary>And how many more would fit.</summary>
	public int Free => ( samples.Length / Channels ) - Queued - 1;

	//Anything that will not fit is dropped rather than waited for: the writer is a frame of the game and the reader is a sound card, and a game that blocks on a sound card stops.
	/// <summary>Add samples, as the game hands them over.</summary>
	public int Write( ReadOnlySpan<short> data )
	{
		var at = write;
		var written = 0;

		var room = Free * Channels;

		for ( var i = 0; i < data.Length && i < room; i++ )
		{
			samples[at] = data[i] / 32768.0f;

			if ( ++at >= samples.Length ) at = 0;
			written++;
		}

		Volatile.Write( ref write, at );

		return written / Channels;
	}

	/// <summary>Add samples that are already floats - a video's soundtrack, decoded - the same way, and with the same dropping of what will not fit.</summary>
	public int Write( ReadOnlySpan<float> data )
	{
		var at = write;
		var written = 0;

		var room = Free * Channels;

		for ( var i = 0; i < data.Length && i < room; i++ )
		{
			samples[at] = data[i];

			if ( ++at >= samples.Length ) at = 0;
			written++;
		}

		Volatile.Write( ref write, at );

		return written / Channels;
	}

	/// <summary>Take frames out, one buffer per channel, resampling to the rate asked for.</summary>
	public int Read( MultiChannelBuffer into, int frames, int outputRate )
	{
		if ( into is null ) return 0;

		var at = read;

		var available = Queued;
		if ( available <= 0 ) return 0;

		//How much of the stream one output frame is worth. A voice at 24k played into a
		//mixer at 44.1k moves through itself slower than the mixer moves through its buffer.
		var ratio = (double)SampleRate / Math.Max( outputRate, 1 );

		var produced = 0;
		var position = phase;

		while ( produced < frames )
		{
			var index = (int)position;
			if ( index >= available ) break;

			//The frame after, for interpolating between the two - except at the very end, where there is nothing after and the
			//last frame stands for itself. Without that a stream keeps one frame forever and never reports itself finished.
			var next = Math.Min( index + 1, available - 1 );

			var fraction = (float)( position - index );

			for ( var channel = 0; channel < into.ChannelCount; channel++ )
			{
				var destination = into.Get( channel ).Buffer;

				var source = Math.Min( channel, Channels - 1 );

				var first = samples[( at + ( index * Channels ) + source ) % samples.Length];
				var second = samples[( at + ( next * Channels ) + source ) % samples.Length];

				destination[produced] = MathX.Lerp( first, second, fraction );
			}

			produced++;
			position += ratio;
		}

		//The whole frames the output has moved past, which is not what was produced when the two rates differ; the fraction left over is where the next read
		//starts. A stream that has been closed and has run out gives up its last frame too, or it would hold it for ever and never report itself finished.
		var consumed = Math.Min( (int)position, available );

		if ( Closed && produced < frames ) consumed = available;

		phase = consumed >= available ? 0.0 : position - consumed;

		Volatile.Write( ref read, ( at + ( consumed * Channels ) ) % samples.Length );

		//Silence for the rest: a stream that ran dry is quiet, not whatever was in the buffer last time.
		for ( var channel = 0; channel < into.ChannelCount; channel++ )
		{
			var destination = into.Get( channel ).Buffer;

			for ( var frame = produced; frame < frames && frame < destination.Length; frame++ )
			{
				destination[frame] = 0.0f;
			}
		}

		return produced;
	}
}
