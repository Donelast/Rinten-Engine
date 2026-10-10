namespace Rinten.Audio;

//Schroeder reverb: 8 combs and 4 allpasses per channel, prime delays, decay set per band
internal sealed class ReverbEffect
{
	//Comb lengths, in samples at 44100. Primes, and spread over about twenty milliseconds, which is a room rather than a cathedral or a tin.
	private static readonly int[] CombLengths = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };

	//And the allpasses that break up what the combs made.
	private static readonly int[] AllpassLengths = { 556, 441, 341, 225 };

	//How much each channel's delays are lengthened, so the two ears are not hearing the same room to the sample.
	private const int ChannelSpread = 23;

	//One delay that feeds itself, with a filter in the loop. The filter is what makes a room sound like a room: high
	//frequencies are swallowed by the walls faster than low ones, so each time round the loop there is less of the top left.
	private sealed class Comb
	{
		private readonly float[] line;
		private int at;

		private float filtered;

		public float Feedback;
		public float Damping;

		public Comb( int length )
		{
			line = new float[Math.Max( length, 1 )];
		}

		public void Silence()
		{
			Array.Clear( line );
			filtered = 0.0f;
		}

		public float Process( float input )
		{
			var output = line[at];

			filtered = ( output * ( 1.0f - Damping ) ) + ( filtered * Damping );

			line[at] = input + ( filtered * Feedback );

			if ( ++at >= line.Length ) at = 0;

			return output;
		}
	}

	//A delay that passes everything through and rearranges when.
	private sealed class Allpass
	{
		private readonly float[] line;
		private int at;

		public Allpass( int length )
		{
			line = new float[Math.Max( length, 1 )];
		}

		public void Silence() => Array.Clear( line );

		public float Process( float input )
		{
			var stored = line[at];
			var output = stored - input;

			//A half is the usual coefficient: enough to smear, not so much that it rings.
			line[at] = input + ( stored * 0.5f );

			if ( ++at >= line.Length ) at = 0;

			return output;
		}
	}

	private Comb[][] combs = Array.Empty<Comb[]>();
	private Allpass[][] allpasses = Array.Empty<Allpass[]>();

	private float lastLow, lastMid, lastHigh;

	//The decay times are how long the room takes to fall away by sixty decibels, one per band. Mid decides how long the tail is; low and high against it decide how the tail is coloured.
	/// <summary>One buffer of room.</summary>
	public void Apply( float decayLow, float decayMid, float decayHigh,
		MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( input is null || output is null ) return;

		var channels = Math.Min( input.ChannelCount, output.ChannelCount );
		if ( channels <= 0 ) return;

		Prepare( channels, decayLow, decayMid, decayHigh );

		var frames = input.Get( 0 ).Buffer.Length;

		for ( var channel = 0; channel < channels; channel++ )
		{
			var source = input.Get( channel ).Buffer;
			var destination = output.Get( channel ).Buffer;

			var channelCombs = combs[channel];
			var channelAllpasses = allpasses[channel];

			for ( var frame = 0; frame < frames; frame++ )
			{
				var sample = source[frame];
				var wet = 0.0f;

				//In parallel, because a comb is one repeat of the room and a room has many at once.
				foreach ( var comb in channelCombs ) wet += comb.Process( sample );

				wet /= channelCombs.Length;

				//And then in series, because smearing something already smeared is what makes a tail rather than a stack of echoes.
				foreach ( var allpass in channelAllpasses ) wet = allpass.Process( wet );

				destination[frame] = wet;
			}
		}
	}

	//Build the delays, and set them to the room being asked for.
	private void Prepare( int channels, float decayLow, float decayMid, float decayHigh )
	{
		if ( combs.Length < channels )
		{
			var grownCombs = new Comb[channels][];
			var grownAllpasses = new Allpass[channels][];

			for ( var channel = 0; channel < channels; channel++ )
			{
				if ( channel < combs.Length )
				{
					grownCombs[channel] = combs[channel];
					grownAllpasses[channel] = allpasses[channel];

					continue;
				}

				//Each channel's room is a little longer than the last, so two ears are not given the same one to the sample.
				var offset = channel * ChannelSpread;

				grownCombs[channel] = new Comb[CombLengths.Length];
				for ( var i = 0; i < CombLengths.Length; i++ ) grownCombs[channel][i] = new Comb( CombLengths[i] + offset );

				grownAllpasses[channel] = new Allpass[AllpassLengths.Length];
				for ( var i = 0; i < AllpassLengths.Length; i++ )
					grownAllpasses[channel][i] = new Allpass( AllpassLengths[i] + offset );
			}

			combs = grownCombs;
			allpasses = grownAllpasses;

			lastMid = -1.0f;
		}

		if ( MathF.Abs( decayLow - lastLow ) < 0.01f
			&& MathF.Abs( decayMid - lastMid ) < 0.01f
			&& MathF.Abs( decayHigh - lastHigh ) < 0.01f )
		{
			return;
		}

		lastLow = decayLow;
		lastMid = decayMid;
		lastHigh = decayHigh;

		//How much has to come back round each time for the tail to last as long as the room says:
		//sixty decibels over the decay time, spread across however long one trip round the delay takes.
		var mid = MathF.Max( decayMid, 0.01f );

		//Damping is how much faster the top goes than the bottom. A room whose high band dies quickly is one with soft walls.
		var ratio = decayLow <= 0.001f ? 1.0f : Math.Clamp( decayHigh / MathF.Max( decayLow, 0.01f ), 0.05f, 1.0f );
		var damping = Math.Clamp( 1.0f - ratio, 0.0f, 0.95f );

		for ( var channel = 0; channel < combs.Length; channel++ )
		{
			foreach ( var comb in combs[channel] )
			{
				comb.Damping = damping;
			}

			for ( var i = 0; i < combs[channel].Length; i++ )
			{
				var length = ( CombLengths[i] + ( channel * ChannelSpread ) ) / AudioEngine.SamplingRate;

				//-60 dB over the decay time, one trip at a time.
				combs[channel][i].Feedback = Math.Clamp( MathF.Pow( 10.0f, -3.0f * length / mid ), 0.0f, 0.98f );
			}
		}
	}

	/// <summary>Empty the room, for a sound that is starting somewhere else.</summary>
	public void Silence()
	{
		foreach ( var channel in combs )
		{
			foreach ( var comb in channel ) comb.Silence();
		}

		foreach ( var channel in allpasses )
		{
			foreach ( var allpass in channel ) allpass.Silence();
		}
	}
}
