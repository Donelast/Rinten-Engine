namespace Rinten.Audio;

//A delay: the sound again, later and quieter, and again after that. One line per channel, read from further back than it is written, with some
//of what comes out fed back in. That feedback is what makes it repeat rather than echo once, and keeping it under one is what makes it stop.
internal sealed class DelayEffect
{
	//The longest delay anything may ask for.
	private const float MaxSeconds = 3.0f;

	private float[][] lines = Array.Empty<float[]>();
	private int at;

	private int length;

	//Where the delay is sliding to, so a knob turned is not a jump.
	private float current = -1.0f;

	//The delay is in seconds and the volume is how much of the repeat comes back - both are what the processor above exposes, and both may move while it plays.
	/// <summary>One buffer of repeats.</summary>
	public void Apply( float delay, float volume, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( input is null || output is null ) return;

		var channels = Math.Min( input.ChannelCount, output.ChannelCount );
		if ( channels <= 0 ) return;

		Prepare( channels );

		delay = Math.Clamp( delay, 0.0f, MaxSeconds );
		volume = Math.Clamp( volume, 0.0f, 0.95f );

		var wanted = MathF.Max( delay * AudioEngine.SamplingRate, 1.0f );

		if ( current < 0.0f ) current = wanted;

		var frames = input.Get( 0 ).Buffer.Length;

		//Slid towards rather than jumped to: moving a read head by a thousand samples between one buffer and the next is a tear.
		var step = ( wanted - current ) / frames;

		var startedAt = at;
		var running = current;

		for ( var channel = 0; channel < channels; channel++ )
		{
			var source = input.Get( channel ).Buffer;
			var destination = output.Get( channel ).Buffer;

			var line = lines[channel];

			at = startedAt;
			running = current;

			for ( var frame = 0; frame < frames; frame++ )
			{
				var back = (int)running;
				var fraction = running - back;

				var first = line[( ( at - back ) % length + length ) % length];
				var second = line[( ( at - back - 1 ) % length + length ) % length];

				var echoed = MathX.Lerp( first, second, fraction );

				//The knob is how loud a repeat is, and each repeat is the one before it through
				//the same knob - so it fades rather than arriving at full volume and then fading.
				line[at % length] = source[frame] + ( echoed * volume );
				destination[frame] = source[frame] + ( echoed * volume );

				at++;
				running += step;
			}
		}

		current = running;
	}

	private void Prepare( int channels )
	{
		var wanted = (int)( MaxSeconds * AudioEngine.SamplingRate ) + 2;

		if ( lines.Length >= channels && length == wanted ) return;

		length = wanted;
		lines = new float[channels][];

		for ( var channel = 0; channel < channels; channel++ ) lines[channel] = new float[length];

		at = 0;
		current = -1.0f;
	}
}
