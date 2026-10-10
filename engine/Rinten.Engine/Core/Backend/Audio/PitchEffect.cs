namespace Rinten.Audio;

//Pitch shift that keeps length: two crossfaded read heads half a window apart (granular)
internal sealed class PitchEffect
{
	//How much sound each head reads before it hands over. About fifty milliseconds. Shorter warbles, longer
	//smears the moment a sound starts - and a footstep that arrives late is worse than one that wavers.
	private const int Window = 2048;

	private float[][] lines = Array.Empty<float[]>();
	private int at;

	private int length;

	//How far each head is behind the write head.
	private float head;

	public void Apply( float pitch, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( input is null || output is null ) return;

		var channels = Math.Min( input.ChannelCount, output.ChannelCount );
		if ( channels <= 0 ) return;

		Prepare( channels );

		pitch = Math.Clamp( pitch, 0.25f, 4.0f );

		var frames = input.Get( 0 ).Buffer.Length;

		//Nothing to do, and nothing to be gained by smearing a sound that was not asked to move.
		if ( MathF.Abs( pitch - 1.0f ) < 0.001f )
		{
			output.CopyFrom( input );

			//Kept fed, so switching it on again does not start from silence.
			for ( var channel = 0; channel < channels; channel++ )
			{
				var source = input.Get( channel ).Buffer;
				var line = lines[channel];

				for ( var frame = 0; frame < frames; frame++ ) line[( at + frame ) % length] = source[frame];
			}

			at = ( at + frames ) % length;
			head = 0.0f;

			return;
		}

		var startedAt = at;
		var startedHead = head;

		for ( var channel = 0; channel < channels; channel++ )
		{
			var source = input.Get( channel ).Buffer;
			var destination = output.Get( channel ).Buffer;

			var line = lines[channel];

			at = startedAt;
			head = startedHead;

			for ( var frame = 0; frame < frames; frame++ )
			{
				line[at % length] = source[frame];

				//Two heads, half a window apart, both moving at the pitch. The one further through its
				//window is fading out as the other fades in, so neither is heard falling off the end.
				var first = Read( line, at, head );
				var second = Read( line, at, ( head + ( Window / 2.0f ) ) % Window );

				var mix = head / Window;

				//Raised cosines, because two straight fades crossing sum to a dip in the middle and the dip is heard as a flutter.
				var firstGain = 0.5f * ( 1.0f - MathF.Cos( 2.0f * MathF.PI * mix ) );
				var secondGain = 1.0f - firstGain;

				destination[frame] = ( first * firstGain ) + ( second * secondGain );

				//How far behind the write head each read head is, and how that changes: reading faster than it is written closes the gap, so a pitch
				//above one walks the heads towards the write head and a pitch below it walks them away. At one they stand still, which is no shift at all.
				head += 1.0f - pitch;

				if ( head >= Window ) head -= Window;
				if ( head < 0.0f ) head += Window;

				at++;
			}
		}

		at %= length;
	}

	//What one head hears, between two samples.
	private float Read( float[] line, int writeAt, float behind )
	{
		var back = (int)behind;
		var fraction = behind - back;

		var first = line[( ( writeAt - back ) % length + length ) % length];
		var second = line[( ( writeAt - back - 1 ) % length + length ) % length];

		return MathX.Lerp( first, second, fraction );
	}

	private void Prepare( int channels )
	{
		var wanted = Window * 4;

		if ( lines.Length >= channels && length == wanted ) return;

		length = wanted;
		lines = new float[channels][];

		for ( var channel = 0; channel < channels; channel++ ) lines[channel] = new float[length];

		at = 0;
		head = 0.0f;
	}
}
