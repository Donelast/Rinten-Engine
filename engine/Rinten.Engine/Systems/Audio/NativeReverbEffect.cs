namespace Rinten.Audio;

//The room a sound is in: one reverb per voice, because a reverb is delay lines with a tail in them,
//and what makes it sound like a room is that this buffer remembers the last one. See ReverbEffect.
sealed class NativeReverbEffect : IDisposable
{
	private readonly ReverbEffect ours = new();

	public void Dispose()
	{
	}

	//Always: the effect is made with this, and is plain managed memory.
	internal bool IsValid => true;

	internal void Apply( float t60Low, float t60Mid, float t60High, MultiChannelBuffer input, MultiChannelBuffer output )
		=> ours.Apply( t60Low, t60Mid, t60High, input, output );
}
