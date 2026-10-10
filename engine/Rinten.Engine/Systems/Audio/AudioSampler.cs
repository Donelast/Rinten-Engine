namespace Rinten.Audio;

//Wraps the native mixer reader: Sample reads once per mix frame, GetSamples lets every mixer read it
internal abstract class AudioSampler : IDisposable
{
	public abstract void Dispose();

	/// <summary>How many channels the sound itself has.</summary>
	public abstract int ChannelCount { get; }

	/// <summary>Whether there is anything to read yet - a stream may still be loading.</summary>
	public abstract bool IsReadyToMix { get; }

	/// <summary>Whether it has anything left to give.</summary>
	public abstract bool ShouldContinueMixing { get; }

	/// <summary>Where the playhead is, in the mixer's own frames.</summary>
	public abstract int SamplePosition { get; set; }

	//Nudge the playhead without moving what has already been read. What keeps a sound that was started late in step with one that was not:
	//the mixer works in whole buffers, and a sound that should have begun in the middle of one begins by skipping that much of itself.
	internal abstract void DelayOrSkipSamples( int numSamples );

	/// <summary>Read one buffer's worth, at a pitch, and advance.</summary>
	public abstract void Sample( float pitch );

	/// <summary>What the last read produced. Read by the mixers, over and over.</summary>
	public abstract MultiChannelBuffer GetLastReadSamples();
}

