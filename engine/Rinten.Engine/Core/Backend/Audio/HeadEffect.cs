namespace Rinten.Audio;

//Cheap head model: interaural delay, level difference and head shadow filtering, no HRTF tables
internal sealed class HeadEffect
{
	//The furthest one ear can be behind the other, in seconds. Half a head is about
	//nine centimetres and sound goes 343 metres a second, which comes out here.
	private const float MaxDelay = 0.00066f;

	//How much of the far ear's top the head takes, at its worst.
	private const float MaxShadow = 0.75f;

	private readonly float[] left = new float[64];
	private readonly float[] right = new float[64];

	private int at;

	private float leftGain, rightGain;
	private float leftShadow, rightShadow;

	private bool started;

	//The direction is in the listener's own space. A blend of zero is a sound that is not anywhere - music, a voice in the player's head - and comes out of both ears as it went in.
	/// <summary>Put a sound somewhere around the listener.</summary>
	public void Apply( Vector3 direction, float blend, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( input is null || output is null || output.ChannelCount < 2 ) return;

		var source = input.Get( 0 ).Buffer;

		var toLeft = output.Get( 0 ).Buffer;
		var toRight = output.Get( 1 ).Buffer;

		blend = Math.Clamp( blend, 0.0f, 1.0f );

		if ( blend <= 0.0f )
		{
			output.CopyFromUpmix( input );
			return;
		}

		var sideways = direction.IsNearZeroLength ? 0.0f : Math.Clamp( Vector3.Left.Dot( direction.Normal ), -1.0f, 1.0f );

		//Behind is not in front, but it is not to one side either: a sound behind the listener is quieter in both ears rather than louder in one.
		var forward = direction.IsNearZeroLength ? 1.0f : Vector3.Forward.Dot( direction.Normal );
		var behind = forward < 0.0f ? 1.0f + ( forward * 0.25f ) : 1.0f;

		//Loudness: the near ear keeps all of it, the far ear keeps some.
		var wantedLeft = MathX.Lerp( 1.0f, Math.Clamp( 0.5f + ( sideways * 0.5f ), 0.15f, 1.0f ) * behind, blend );
		var wantedRight = MathX.Lerp( 1.0f, Math.Clamp( 0.5f - ( sideways * 0.5f ), 0.15f, 1.0f ) * behind, blend );

		//Shadow: how much of the far ear's top the head takes.
		var wantedLeftShadow = MathF.Max( -sideways, 0.0f ) * MaxShadow * blend;
		var wantedRightShadow = MathF.Max( sideways, 0.0f ) * MaxShadow * blend;

		if ( !started )
		{
			leftGain = wantedLeft;
			rightGain = wantedRight;

			started = true;
		}

		//Delay: whichever ear the sound reaches second waits.
		var samples = MaxDelay * AudioEngine.SamplingRate * blend;

		var leftDelay = sideways < 0.0f ? -sideways * samples : 0.0f;
		var rightDelay = sideways > 0.0f ? sideways * samples : 0.0f;

		var frames = source.Length;

		var leftStep = ( wantedLeft - leftGain ) / frames;
		var rightStep = ( wantedRight - rightGain ) / frames;

		var leftRunning = leftGain;
		var rightRunning = rightGain;

		for ( var frame = 0; frame < frames; frame++ )
		{
			var sample = source[frame];

			//One ring per ear, long enough for the widest head.
			left[at & 63] = sample;
			right[at & 63] = sample;

			toLeft[frame] = Shadow( Delayed( left, at, leftDelay ), ref leftShadow, wantedLeftShadow ) * leftRunning;
			toRight[frame] = Shadow( Delayed( right, at, rightDelay ), ref rightShadow, wantedRightShadow ) * rightRunning;

			leftRunning += leftStep;
			rightRunning += rightStep;

			at++;
		}

		leftGain = wantedLeft;
		rightGain = wantedRight;
	}

	//A sample from a fraction of a sample ago. Between two of them, because the delay that carries the direction
	//is a fraction of a sample wide and rounding it to a whole one turns a smooth pan into a staircase.
	private static float Delayed( float[] line, int at, float delay )
	{
		var whole = (int)delay;
		var fraction = delay - whole;

		var first = line[( at - whole ) & 63];
		var second = line[( at - whole - 1 ) & 63];

		return MathX.Lerp( first, second, fraction );
	}

	//The head in the way, as a one-pole that keeps the bottom.
	private static float Shadow( float sample, ref float state, float amount )
	{
		if ( amount <= 0.0f )
		{
			state = sample;
			return sample;
		}

		state += ( sample - state ) * ( 1.0f - amount );

		return state;
	}

	public void Reset()
	{
		Array.Clear( left );
		Array.Clear( right );

		leftShadow = 0.0f;
		rightShadow = 0.0f;

		started = false;
	}
}
