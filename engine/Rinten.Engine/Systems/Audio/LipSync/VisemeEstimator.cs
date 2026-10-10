namespace Rinten.Audio;

//Estimates 15 viseme weights from voice audio via loudness, bands and formants, not recognition
//Allocation-free and deterministic, built for the mixing thread
internal sealed class VisemeEstimator
{
	/// <summary>How many samples one analysis looks at - one mix buffer.</summary>
	public const int BlockSize = 512;

	const int Bins = BlockSize / 2 + 1;

	//Indices into the weights, named for reading the rules below.
	const int Sil = 0, PP = 1, FF = 2, TH = 3, DD = 4, KK = 5, CH = 6, SS = 7, NN = 8, RR = 9, AA = 10, E = 11, IH = 12, OH = 13, OU = 14;

	//Where each vowel sits on the first and second formant, in hertz - an average adult voice, which is all a face needs. RR sits
	//with the vowels because to an ear it behaves like one: voiced, with a low third formant that pulls the second one down.
	static readonly (int Viseme, float F1, float F2)[] Vowels =
	{
		(AA, 750.0f, 1250.0f),
		(E, 550.0f, 1850.0f),
		(IH, 360.0f, 2150.0f),
		(OH, 520.0f, 920.0f),
		(OU, 340.0f, 800.0f),
		(RR, 470.0f, 1350.0f),
	};

	readonly float sampleRate;
	readonly float blockSeconds;

	readonly float[] window;
	readonly float[] re = new float[BlockSize];
	readonly float[] im = new float[BlockSize];
	readonly float[] power = new float[Bins];

	readonly float[] target = new float[Visemes.Count];
	readonly float[] weights = new float[Visemes.Count];

	//The bands, as bin ranges worked out once for this rate.
	readonly int lowFrom, lowTo, firstFrom, firstTo, secondFrom, secondTo, highFrom, highTo, hissFrom, hissTo;

	//Where the first and second formants are looked for, as bin ranges.
	readonly int f1From, f1To, f2Low, f2High;

	//How much a weight moves towards its target per block, opening and closing.
	readonly float attack, release;

	//How fast the voice's peak falls away, and the noise floor creeps up, per block.
	readonly float peakFall, floorRise;

	float peak;
	float floor;
	float previousDb;
	float previousActivity;

	//Syllable onsets, as block numbers, for the laughter guess.
	readonly long[] onsets = new long[8];
	int onsetAt;
	long block;
	float breath;
	float laughter;

	/// <summary>The weights after the last block, in <see cref="Visemes.MorphNames"/> order.</summary>
	public ReadOnlySpan<float> Weights => weights;

	/// <summary>How much the last second or so sounded like laughing, 0 to 1.</summary>
	public float Laughter => laughter;

	/// <summary>How far behind the sound the weights are, in milliseconds: half a block plus the attack.</summary>
	public int DelayMilliseconds => (int)( ( blockSeconds * 0.5f + AttackSeconds ) * 1000.0f );

	const float AttackSeconds = 0.025f;
	const float ReleaseSeconds = 0.07f;

	public VisemeEstimator( float sampleRate )
	{
		this.sampleRate = sampleRate > 0.0f ? sampleRate : 44100.0f;
		blockSeconds = BlockSize / this.sampleRate;

		window = Fourier.Hann( BlockSize );

		(lowFrom, lowTo) = Band( 80.0f, 300.0f );
		(firstFrom, firstTo) = Band( 300.0f, 1000.0f );
		(secondFrom, secondTo) = Band( 1000.0f, 2600.0f );
		(highFrom, highTo) = Band( 2600.0f, 5000.0f );
		(hissFrom, hissTo) = Band( 5000.0f, 11000.0f );

		(f1From, f1To) = Band( 250.0f, 1000.0f );
		(f2Low, f2High) = Band( 800.0f, 2800.0f );

		attack = 1.0f - MathF.Exp( -blockSeconds / AttackSeconds );
		release = 1.0f - MathF.Exp( -blockSeconds / ReleaseSeconds );

		//A peak halves in about four seconds of quiet, so the gain follows a speaker who moves away but not the gaps between words. The
		//floor climbs about 6 dB a second while there is sound, and drops at once when there is less: it settles on the quietest thing heard.
		peakFall = MathF.Pow( 0.5f, blockSeconds / 4.0f );
		floorRise = MathF.Pow( 2.0f, blockSeconds );

		Reset();
	}

	/// <summary>Forget the voice: silence, and a gain and floor learned from scratch.</summary>
	public void Reset()
	{
		Array.Clear( target );
		Array.Clear( weights );
		Array.Clear( onsets );

		weights[Sil] = 1.0f;
		peak = 0.05f;
		floor = 0.0003f;
		previousDb = -120.0f;
		previousActivity = 0.0f;
		onsetAt = 0;
		block = 0;
		breath = 0.0f;
		laughter = 0.0f;
	}

	(int From, int To) Band( float low, float high )
	{
		var hz = sampleRate / BlockSize;
		var nyquist = Bins - 1;

		var from = Math.Clamp( (int)MathF.Ceiling( low / hz ), 1, nyquist );
		var to = Math.Clamp( (int)MathF.Floor( high / hz ), from, nyquist );

		return (from, to);
	}

	float Sum( int from, int to )
	{
		var sum = 0.0f;
		for ( var i = from; i <= to; i++ ) sum += power[i];
		return sum;
	}

	//The strongest resonance between two bins, in hertz, placed between bins by fitting a parabola through the peak and its neighbours.
	float Resonance( int from, int to )
	{
		var best = from;
		var bestPower = -1.0f;

		for ( var i = from; i <= to; i++ )
		{
			//Three bins summed, so one stray harmonic does not win over the hump of the resonance it sits on.
			var smoothed = power[i - 1] + power[i] + power[Math.Min( i + 1, Bins - 1 )];

			if ( smoothed > bestPower )
			{
				bestPower = smoothed;
				best = i;
			}
		}

		var a = power[best - 1];
		var b = power[best];
		var c = power[Math.Min( best + 1, Bins - 1 )];
		var bend = a - ( 2.0f * b ) + c;
		var offset = MathF.Abs( bend ) > 1e-12f ? Math.Clamp( 0.5f * ( a - c ) / bend, -0.5f, 0.5f ) : 0.0f;

		return ( best + offset ) * sampleRate / BlockSize;
	}

	static float Saturate( float value ) => value < 0.0f ? 0.0f : value > 1.0f ? 1.0f : value;

	static float Decibels( float linear ) => 20.0f * MathF.Log10( linear + 1e-9f );

	/// <summary>Listen to the next block of mono samples - up to <see cref="BlockSize"/> of them, the rest taken as silence - and move the weights on by one.</summary>
	public void Process( ReadOnlySpan<float> samples )
	{
		var count = Math.Min( samples.Length, BlockSize );
		block++;

		//Loudness and zero crossings, which are free on the way to the window.
		var energy = 0.0f;
		var crossings = 0;
		var last = 0.0f;

		for ( var i = 0; i < BlockSize; i++ )
		{
			var sample = i < count ? samples[i] : 0.0f;

			//A sample that is not a number would poison every weight for as long as the voice plays; it is heard as nothing instead.
			if ( !float.IsFinite( sample ) ) sample = 0.0f;

			energy += sample * sample;

			if ( i > 0 && ( ( sample >= 0.0f ) != ( last >= 0.0f ) ) ) crossings++;
			last = sample;

			re[i] = sample * window[i];
			im[i] = 0.0f;
		}

		var rms = MathF.Sqrt( energy / BlockSize );
		var db = Decibels( rms );
		var zeroCrossing = crossings / (float)( BlockSize - 1 );

		//The gain and the floor.
		peak = MathF.Max( rms, MathF.Max( peak * peakFall, 0.01f ) );
		floor = rms < floor ? MathF.Max( rms, 1e-5f ) : floor * floorRise;

		//Speaking when well clear of both the floor and plain quiet: -50 dBFS, or 10 dB over the room, whichever is
		//higher. Eased over 12 dB rather than switched, so a word fading out closes the mouth rather than snapping it shut.
		var gateDb = MathF.Max( -50.0f, Decibels( floor ) + 10.0f );
		var activity = Saturate( ( db - gateDb ) / 12.0f );

		//How open: where this sits in the 30 dB under the voice's own peak.
		var openness = Saturate( ( db - ( Decibels( peak ) - 30.0f ) ) / 30.0f );

		Array.Clear( target );

		var rise = db - previousDb;
		var onset = previousActivity < 0.3f && activity > 0.5f && rise > 6.0f;

		if ( activity > 0.0f )
		{
			Fourier.Transform( re, im );

			for ( var i = 0; i < Bins; i++ ) power[i] = ( re[i] * re[i] ) + ( im[i] * im[i] );

			var low = Sum( lowFrom, lowTo );
			var first = Sum( firstFrom, firstTo );
			var second = Sum( secondFrom, secondTo );
			var high = Sum( highFrom, highTo );
			var hiss = Sum( hissFrom, hissTo );
			var total = low + first + second + high + hiss + 1e-12f;

			var centroidSum = 0.0f;
			var centroidWeight = 0.0f;

			for ( var i = lowFrom; i <= hissTo; i++ )
			{
				centroidSum += i * power[i];
				centroidWeight += power[i];
			}

			var centroid = centroidWeight > 0.0f ? centroidSum / centroidWeight * sampleRate / BlockSize : 0.0f;

			var hissShare = ( high + hiss ) / total;
			var lowShare = low / total;

			//Fricative: most of the energy above 2.6 kHz, or a waveform crossing zero far more often than a voice's harmonics do.
			var fricative = Saturate( MathF.Max( ( hissShare - 0.3f ) / 0.4f, ( zeroCrossing - 0.12f ) / 0.2f ) );
			var voiced = 1.0f - fricative;

			//Plosive: a jump out of quiet. The burst's centre says where the closure was - lips low, the back of the tongue in the middle, the tip high.
			var plosive = previousActivity < 0.3f ? Saturate( ( rise - 9.0f ) / 9.0f ) : 0.0f;

			//Nasal: voiced, almost all low band, and next to nothing where the second formant would be - the mouth is shut and the sound is coming
			//out of the nose. A close front vowel (ih) is low-heavy too, but keeps a strong second formant, which is what tells them apart.
			var upperShare = ( second + high ) / total;
			var nasal = voiced * Saturate( ( lowShare - 0.5f ) / 0.3f ) * Saturate( ( 0.03f - upperShare ) / 0.025f ) * ( 1.0f - openness * 0.5f );

			var vowel = MathF.Max( 0.0f, voiced - nasal ) * ( 1.0f - plosive );
			var consonant = fricative * ( 1.0f - plosive );

			//The mouth is only as shaped as the voice is loud; the rest is rest.
			var speech = activity * ( 0.35f + 0.65f * openness );

			if ( vowel > 0.0f )
			{
				//The second formant is looked for clear of the first, or a wide first formant would be found twice.
				var f1 = Resonance( f1From, f1To );
				var f2From = Math.Clamp( (int)( ( f1 + 250.0f ) * BlockSize / sampleRate ), f2Low, f2High );
				var f2 = Resonance( f2From, f2High );

				var sum = 0.0f;

				foreach ( var (viseme, p1, p2) in Vowels )
				{
					//Distance on a log scale, which is how far apart two vowels sound: 100 Hz is a lot at the bottom and little at the top.
					var d1 = MathF.Log2( MathF.Max( f1, 50.0f ) / p1 ) / 0.35f;
					var d2 = MathF.Log2( MathF.Max( f2, 50.0f ) / p2 ) / 0.3f;
					var membership = MathF.Exp( -0.5f * ( ( d1 * d1 ) + ( d2 * d2 ) ) );

					target[viseme] = membership;
					sum += membership;
				}

				//Nowhere near any of them - a murmur - reads as a relaxed aa.
				if ( sum < 1e-3f )
				{
					target[AA] = 1.0f;
					sum = 1.0f;
				}

				var scale = vowel * speech / sum;
				foreach ( var (viseme, _, _) in Vowels ) target[viseme] *= scale;
			}

			if ( consonant > 0.0f )
			{
				var sharp = Saturate( ( centroid - 3500.0f ) / 3000.0f );
				var strong = openness;

				target[SS] += consonant * speech * strong * sharp;
				target[CH] += consonant * speech * strong * ( 1.0f - sharp );
				target[FF] += consonant * speech * ( 1.0f - strong ) * 0.6f;
				target[TH] += consonant * speech * ( 1.0f - strong ) * 0.4f;
			}

			if ( plosive > 0.0f )
			{
				var lips = Saturate( ( 1800.0f - centroid ) / 600.0f );
				var tip = Saturate( ( centroid - 3000.0f ) / 1000.0f );
				var back = MathF.Max( 0.0f, 1.0f - lips - tip );

				target[PP] += plosive * speech * lips;
				target[KK] += plosive * speech * back;
				target[DD] += plosive * speech * tip;
			}

			target[NN] += nasal * speech;

			//Breathiness while voiced, for the laughter guess: laughs are voiced bursts with air through them.
			breath += ( ( hissShare * voiced ) - breath ) * 0.1f;
		}

		//Whatever the shapes do not claim is the mouth at rest.
		var claimed = 0.0f;
		for ( var i = 1; i < target.Length; i++ ) claimed += target[i];

		if ( claimed > 1.0f )
		{
			for ( var i = 1; i < target.Length; i++ ) target[i] /= claimed;
			claimed = 1.0f;
		}

		target[Sil] = 1.0f - claimed;

		var mouth = 0.0f;

		for ( var i = 0; i < weights.Length; i++ )
		{
			var goal = target[i];
			var rate = goal > weights[i] ? attack : release;
			weights[i] += ( goal - weights[i] ) * rate;
			mouth += weights[i];
		}

		//Opening faster than closing lets a change of shape briefly add up to more than one whole mouth; scaled back so it never does.
		if ( mouth > 1.0f )
		{
			for ( var i = 0; i < weights.Length; i++ ) weights[i] /= mouth;
		}

		if ( onset )
		{
			onsets[onsetAt] = block;
			onsetAt = ( onsetAt + 1 ) % onsets.Length;
		}

		UpdateLaughter();

		previousDb = db;
		previousActivity = activity;
	}

	//A guess at laughing: syllables bursting out three to seven times a second, evenly - "ha-ha-ha" - scored higher the more air
	//there is in them. It has no model of a laugh behind it, only that rhythm, so fast even speech reads as a little laughter too.
	void UpdateLaughter()
	{
		var windowBlocks = (long)( 1.5f / blockSeconds );

		var count = 0;
		var newest = long.MinValue;
		var oldest = long.MaxValue;

		foreach ( var at in onsets )
		{
			if ( at <= 0 || block - at > windowBlocks ) continue;

			count++;
			newest = Math.Max( newest, at );
			oldest = Math.Min( oldest, at );
		}

		var score = 0.0f;

		if ( count >= 3 )
		{
			//The mean gap between them, and how much each gap strays from it.
			var mean = ( newest - oldest ) / (float)( count - 1 ) * blockSeconds;
			var spread = 0.0f;

			foreach ( var at in onsets )
			{
				if ( at <= 0 || block - at > windowBlocks ) continue;

				//The nearest onset after this one, which is its gap.
				var next = long.MaxValue;
				foreach ( var other in onsets )
					if ( other > at && other < next && block - other <= windowBlocks ) next = other;

				if ( next == long.MaxValue ) continue;

				var gap = ( next - at ) * blockSeconds - mean;
				spread += gap * gap;
			}

			var variation = mean > 0.0f ? MathF.Sqrt( spread / ( count - 1 ) ) / mean : 1.0f;

			var rhythm = mean is >= 0.14f and <= 0.3f ? Saturate( 1.0f - variation / 0.35f ) : 0.0f;
			score = rhythm * ( 0.4f + ( 0.6f * Saturate( ( breath - 0.02f ) / 0.1f ) ) );
		}

		//Up fast, down over a third of a second: a laugh is not over between its syllables.
		var rate = score > laughter ? 0.5f : 1.0f - MathF.Exp( -blockSeconds / 0.3f );
		laughter += ( score - laughter ) * rate;
	}
}
