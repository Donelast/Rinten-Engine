namespace Rinten.Audio;

//A legacy room effect graph ("tunnel.medium" etc.) rebuilt from the same processors and parameters
internal sealed class DspChain
{
	//What a processor does to one channel.
	private interface IProcessor
	{
		float Process( float sample );
	}

	//A delay line with a read head some distance back.
	private sealed class Line
	{
		private readonly float[] samples;
		private int at;

		public Line( int length )
		{
			samples = new float[Math.Max( length, 1 )];
		}

		public int Length => samples.Length;

		public float Read( float back )
		{
			var whole = (int)back;
			var fraction = back - whole;

			var first = samples[( ( at - whole ) % samples.Length + samples.Length ) % samples.Length];
			var second = samples[( ( at - whole - 1 ) % samples.Length + samples.Length ) % samples.Length];

			return MathX.Lerp( first, second, fraction );
		}

		public void Write( float sample )
		{
			samples[at] = sample;

			if ( ++at >= samples.Length ) at = 0;
		}
	}

	//A one-pole, which is all the filtering these rooms ever wanted.
	private struct OnePole
	{
		private float state;
		private float coefficient;

		public void SetCutoff( float frequency )
		{
			var wanted = Math.Clamp( frequency, 20.0f, AudioEngine.SamplingRate * 0.45f );

			coefficient = MathF.Exp( -2.0f * MathF.PI * wanted / AudioEngine.SamplingRate );
		}

		public float Low( float sample )
		{
			state = ( sample * ( 1.0f - coefficient ) ) + ( state * coefficient );
			return state;
		}

		public float High( float sample ) => sample - Low( sample );
	}

	//Delays in parallel, each feeding itself through a filter. The room. Lengths are spread between the shortest and the
	//longest the preset asked for, and the filter in each loop is what makes the tail get duller rather than only quieter.
	private sealed class Reverb : IProcessor
	{
		private readonly Line[] lines;
		private readonly float[] lengths;
		private OnePole[] filters;

		private readonly float feedback;
		private readonly float gain;
		private readonly bool parallel;

		private readonly float modDepth;
		private readonly float modRate;

		private float phase;

		public Reverb( float[] values )
		{
			var maximum = Value( values, 0, 80.0f );
			var minimum = Value( values, 1, 30.0f );

			var count = Math.Clamp( (int)Value( values, 2, 4.0f ), 1, 16 );

			feedback = Math.Clamp( Value( values, 3, 0.85f ), 0.0f, 0.97f );
			gain = Value( values, 4, 1.0f );

			var cutoff = Value( values, 5, 4000.0f );

			parallel = Value( values, 6, 1.0f ) != 0.0f;

			modDepth = Value( values, 7, 0.0f ) * AudioEngine.SamplingRate / 1000.0f;
			modRate = Value( values, 8, 0.0f );

			lines = new Line[count];
			lengths = new float[count];
			filters = new OnePole[count];

			for ( var i = 0; i < count; i++ )
			{
				//Spread between the two, and never on a round number: delays that share a factor ring instead of filling.
				var fraction = count == 1 ? 0.0f : i / (float)( count - 1 );
				var milliseconds = MathX.Lerp( minimum, maximum, fraction ) + ( i * 0.37f );

				lengths[i] = milliseconds * AudioEngine.SamplingRate / 1000.0f;
				lines[i] = new Line( (int)lengths[i] + (int)modDepth + 4 );

				filters[i].SetCutoff( cutoff );
			}
		}

		public float Process( float sample )
		{
			var wobble = 0.0f;

			if ( modRate > 0.0f && modDepth > 0.0f )
			{
				phase += 2.0f * MathF.PI * modRate / AudioEngine.SamplingRate;
				if ( phase > 2.0f * MathF.PI ) phase -= 2.0f * MathF.PI;

				wobble = MathF.Sin( phase ) * modDepth;
			}

			var total = 0.0f;
			var carried = sample;

			for ( var i = 0; i < lines.Length; i++ )
			{
				var delayed = lines[i].Read( lengths[i] + wobble );
				var filtered = filters[i].Low( delayed );

				lines[i].Write( ( parallel ? sample : carried ) + ( filtered * feedback ) );

				if ( parallel ) total += delayed;
				else carried = delayed;
			}

			var wet = parallel ? total / lines.Length : carried;

			return wet * gain;
		}
	}

	//Allpasses in a row: everything comes through, in a different order. What stands between a stack of echoes and a tail.
	//On its own it barely changes what a sound is; in front of a reverb it is the difference between a room and a pipe.
	private sealed class Diffusor : IProcessor
	{
		private readonly Line[] lines;
		private readonly float[] lengths;

		private readonly float feedback;
		private readonly float gain;

		public Diffusor( float[] values )
		{
			var scale = Value( values, 0, 1.0f );
			var count = Math.Clamp( (int)Value( values, 1, 3.0f ), 1, 8 );

			feedback = Math.Clamp( Value( values, 2, 0.15f ), 0.0f, 0.9f );
			gain = Value( values, 3, 0.0f );

			lines = new Line[count];
			lengths = new float[count];

			//Short, and getting shorter: the first spreads, the rest fill in.
			var milliseconds = 13.0f * scale;

			for ( var i = 0; i < count; i++ )
			{
				lengths[i] = milliseconds * AudioEngine.SamplingRate / 1000.0f;
				lines[i] = new Line( (int)lengths[i] + 4 );

				milliseconds *= 0.68f;
			}
		}

		public float Process( float sample )
		{
			var carried = sample;

			for ( var i = 0; i < lines.Length; i++ )
			{
				var delayed = lines[i].Read( lengths[i] );

				lines[i].Write( carried + ( delayed * feedback ) );

				carried = delayed - ( carried * feedback );
			}

			return gain > 0.0f ? carried * gain : carried;
		}
	}

	//Loudness, roughness, and a wobble. The distortion is a soft clip rather than a hard one: hard clipping
	//is a buzz and soft clipping is a voice through a speaker, which is what these presets use it for.
	private sealed class Amplifier : IProcessor
	{
		private readonly float gain;
		private readonly float threshold;
		private readonly float mix;

		private readonly float modRate;
		private readonly float modDepth;

		private float phase;

		public Amplifier( float[] values )
		{
			gain = Value( values, 0, 1.0f );
			threshold = MathF.Max( Value( values, 1, 1.0f ), 0.0001f );
			mix = Math.Clamp( Value( values, 2, 0.0f ), 0.0f, 1.0f );

			modRate = Value( values, 4, 0.0f );
			modDepth = Math.Clamp( Value( values, 5, 0.0f ), 0.0f, 1.0f );
		}

		public float Process( float sample )
		{
			var amplified = sample * gain;

			if ( mix > 0.0f )
			{
				//Above the threshold it bends rather than breaks.
				var bent = MathF.Tanh( amplified / threshold ) * threshold;
				amplified = MathX.Lerp( amplified, bent, mix );
			}

			if ( modRate <= 0.0f || modDepth <= 0.0f ) return amplified;

			phase += 2.0f * MathF.PI * modRate / AudioEngine.SamplingRate;
			if ( phase > 2.0f * MathF.PI ) phase -= 2.0f * MathF.PI;

			return amplified * ( 1.0f - ( modDepth * 0.5f * ( 1.0f - MathF.Cos( phase ) ) ) );
		}
	}

	//One delay, with a filter in the loop and up to three extra taps.
	private sealed class Delay : IProcessor
	{
		private readonly Line line;
		private readonly float length;

		private readonly float feedback;
		private readonly float gain;

		private readonly float[] taps;

		private OnePole filter;
		private readonly bool highPass;

		private readonly float modDepth;
		private readonly float modRate;

		private float phase;

		public Delay( float[] values, bool modulated )
		{
			length = MathF.Max( Value( values, 1, 100.0f ), 1.0f ) * AudioEngine.SamplingRate / 1000.0f;

			feedback = Math.Clamp( Value( values, 2, 0.0f ), 0.0f, 0.95f );
			gain = Value( values, 3, 1.0f );

			var filterType = (int)Value( values, 4, 0.0f );
			highPass = filterType == 1;

			filter.SetCutoff( Value( values, 5, 8000.0f ) );

			if ( modulated )
			{
				modRate = Value( values, 8, 0.0f );
				modDepth = Value( values, 9, 0.0f ) * AudioEngine.SamplingRate / 1000.0f;

				taps = Array.Empty<float>();
			}
			else
			{
				//Extra reads off the same line, as fractions of the delay.
				var extra = new List<float>();

				for ( var i = 8; i <= 10 && i < values.Length; i++ )
				{
					if ( values[i] > 0.0f ) extra.Add( values[i] * AudioEngine.SamplingRate / 1000.0f );
				}

				taps = extra.ToArray();
			}

			var longest = length + modDepth;
			foreach ( var tap in taps ) longest = MathF.Max( longest, tap );

			line = new Line( (int)longest + 4 );
		}

		public float Process( float sample )
		{
			var wobble = 0.0f;

			if ( modRate > 0.0f && modDepth > 0.0f )
			{
				phase += 2.0f * MathF.PI * modRate / AudioEngine.SamplingRate;
				if ( phase > 2.0f * MathF.PI ) phase -= 2.0f * MathF.PI;

				wobble = MathF.Sin( phase ) * modDepth;
			}

			var delayed = line.Read( length + wobble );

			foreach ( var tap in taps ) delayed += line.Read( tap );

			var filtered = highPass ? filter.High( delayed ) : filter.Low( delayed );

			line.Write( sample + ( filtered * feedback ) );

			return sample + ( delayed * gain );
		}
	}

	//A filter on its own.
	private sealed class Filter : IProcessor
	{
		private OnePole pole;

		private readonly int type;
		private readonly float gain;

		public Filter( float[] values )
		{
			type = (int)Value( values, 0, 0.0f );

			pole.SetCutoff( Value( values, 1, 8000.0f ) );

			gain = Value( values, 4, 1.0f );
		}

		public float Process( float sample )
		{
			var filtered = type switch
			{
				1 => pole.High( sample ),
				2 => pole.High( pole.Low( sample ) ),
				_ => pole.Low( sample ),
			};

			return gain != 0.0f ? filtered * gain : filtered;
		}
	}

	private static float Value( float[] values, int index, float fallback )
		=> values is not null && index < values.Length ? values[index] : fallback;

	private readonly IProcessor[][] processors;

	//Per channel because every one of these remembers something - a delay line, a filter's last sample - and two channels sharing that would be one ear hearing the other's room.
	/// <summary>Build a chain, one set of processors per channel.</summary>
	public DspChain( IReadOnlyList<(int Kind, float[] Values)> definition, int channels )
	{
		channels = Math.Clamp( channels, 1, 8 );

		processors = new IProcessor[channels][];

		for ( var channel = 0; channel < channels; channel++ )
		{
			var built = new List<IProcessor>();

			foreach ( var (kind, values) in definition ?? Array.Empty<(int, float[])>() )
			{
				IProcessor processor = kind switch
				{
					1 => new Delay( values, modulated: false ),
					2 => new Reverb( values ),
					3 => new Filter( values ),
					9 => new Delay( values, modulated: true ),
					10 => new Diffusor( values ),
					11 => new Amplifier( values ),
					_ => null,
				};

				if ( processor is not null ) built.Add( processor );
			}

			processors[channel] = built.ToArray();
		}
	}

	public void Process( MixBuffer input, MixBuffer output, int channel )
	{
		if ( input is null || output is null ) return;
		if ( channel < 0 || channel >= processors.Length ) return;

		var source = input.Buffer;
		var destination = output.Buffer;

		var chain = processors[channel];

		for ( var frame = 0; frame < source.Length; frame++ )
		{
			var sample = source[frame];

			foreach ( var processor in chain ) sample = processor.Process( sample );

			destination[frame] = sample;
		}
	}
}
