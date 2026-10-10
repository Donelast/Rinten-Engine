using Rinten.Audio;

namespace Rinten;

/// <summary>Streams music from files or the web through a SoundHandle, with seeking and spectrum; Dispose it</summary>
public sealed class MusicPlayer : IDisposable
{
	//Every player that has not been disposed, so they hear when their song ends - see TickAll.
	private static readonly List<WeakReference<MusicPlayer>> playing = new();

	private CoreSound sound;
	private CoreAudioSampler sampler;
	private SoundHandle handle;

	private int loopsSeen;
	private bool finishedSaid;

	//Where the song came from, for a failure to name.
	private string source;

	//What Spectrum hands out, and the scratch its transform works in - kept, so that a visualiser asking every frame allocates nothing.
	private float[] spectrum;
	private float[] window;
	private float[] real;
	private float[] imaginary;

	//What the handle is set to before there is one - a song from the web is played once
	//it has arrived, and whatever was said about it in between is still wanted then.
	private float volume = 1.0f;
	private bool paused;
	private bool repeat;
	private bool listenLocal;
	private Vector3 position;
	private bool lipSync;
	private Mixer targetMixer;
	private float distance = 15_000f;
	private Curve? falloff;

	/// <summary>Sample rate of the song, as it was written.</summary>
	public int SampleRate { get; private set; }

	/// <summary>Number of channels of the song.</summary>
	public int Channels { get; private set; }

	/// <summary>How long the song is, in seconds. Nought until it has loaded, or for a stream that does not say.</summary>
	public float Duration => sound?.Duration ?? 0.0f;

	/// <summary>Where in the song it is, in seconds - within the song, however many times it has gone round.</summary>
	public float PlaybackTime
	{
		get
		{
			if ( !handle.IsValid() ) return 0.0f;

			var time = handle.Time;
			var duration = Duration;

			return duration > 0.0f ? time % duration : time;
		}
	}

	/// <summary>Invoked on the main thread when the song has played to its end.</summary>
	public Action OnFinished { get; set; }

	/// <summary>Invoked on the main thread each time a repeating song goes back to its start.</summary>
	public Action OnRepeated { get; set; }

	/// <summary>Heard where the listener is rather than where the song is placed - the usual thing for music. On by default.</summary>
	public bool ListenLocal
	{
		get => listenLocal;
		set
		{
			listenLocal = value;
			if ( handle.IsValid() ) handle.ListenLocal = value;
		}
	}

	/// <summary>Where the song is in the world, for one that is not <see cref="ListenLocal"/>.</summary>
	public Vector3 Position
	{
		get => position;
		set
		{
			position = value;
			if ( handle.IsValid() ) handle.Position = value;
		}
	}

	/// <summary>Stops the song where it is, or carries on from there.</summary>
	public bool Paused
	{
		get => paused;
		set
		{
			paused = value;
			if ( handle.IsValid() ) handle.Paused = value;
		}
	}

	/// <summary>Goes back to the start at the end, rather than finishing.</summary>
	public bool Repeat
	{
		get => repeat;
		set
		{
			repeat = value;
			if ( sampler is not null ) sampler.Looping = value;
		}
	}

	/// <summary>How loud, one being as it was written.</summary>
	public float Volume
	{
		get => volume;
		set
		{
			volume = value;
			if ( handle.IsValid() ) handle.Volume = value;
		}
	}

	/// <summary>Works out mouth shapes from the song as it plays - see <see cref="Visemes"/>.</summary>
	public bool LipSync
	{
		get => lipSync;
		set
		{
			lipSync = value;
			if ( handle.IsValid() ) handle.LipSync.Enabled = value;
		}
	}

	/// <summary>Which mixer the song plays through; the default one where this is null.</summary>
	public Mixer TargetMixer
	{
		get => targetMixer;
		set
		{
			targetMixer = value;
			if ( handle.IsValid() ) handle.TargetMixer = value;
		}
	}

	public float Distance
	{
		get => distance;
		set
		{
			distance = value;
			if ( handle.IsValid() ) handle.Distance = value;
		}
	}

	public Curve Falloff
	{
		get => falloff ?? ( handle.IsValid() ? handle.Falloff : default );
		set
		{
			falloff = value;
			if ( handle.IsValid() ) handle.Falloff = value;
		}
	}

	/// <summary>The fifteen viseme weights, while <see cref="LipSync"/> is on.</summary>
	public IReadOnlyList<float> Visemes => handle.IsValid() ? handle.LipSync.Visemes : Array.Empty<float>();

	/// <summary>The song's title, as its tags say - or a web stream's current title.</summary>
	public string Title
	{
		get
		{
			var title = GetMeta( "title" );
			return string.IsNullOrWhiteSpace( title ) ? GetMeta( "StreamTitle" ) : title;
		}
	}

	/// <summary>Who made it, as its tags say.</summary>
	public string Artist => GetMeta( "artist" );

	/// <summary>How loud it is now, roughly: the peak of what last went out.</summary>
	public float Amplitude => handle.IsValid() ? handle.Amplitude : 0.0f;

	/// <summary>How strong each frequency is in what is playing now: 512 magnitudes from nothing to half the song's sample rate, for something to draw. Empty for the first frame it is asked for, while it starts listening.</summary>
	public ReadOnlySpan<float> Spectrum => GetSpectrum();

	private MusicPlayer( string source )
	{
		this.source = source;
		listenLocal = true;
	}

	/// <summary>Plays a song from the game's files - wav, mp3, flac or ogg. Null if the file is not there or is not a song this can play.</summary>
	public static MusicPlayer Play( BaseFileSystem filesystem, string path )
	{
		ThreadSafe.AssertIsMainThread();

		if ( filesystem is null || string.IsNullOrWhiteSpace( path ) ) return null;

		if ( !filesystem.FileExists( path ) )
		{
			Log.Warning( $"music: {path} is not there" );
			return null;
		}

		var player = new MusicPlayer( path );

		return player.Start( filesystem.ReadAllBytes( path ) ) ? player : null;
	}

	/// <summary>Plays a song from an allowed URL, fetched whole first, so endless streams aren't supported</summary>
	public static MusicPlayer PlayUrl( string url )
	{
		ThreadSafe.AssertIsMainThread();

		if ( string.IsNullOrWhiteSpace( url ) ) return null;

		var player = new MusicPlayer( url );
		_ = player.Fetch( url );

		return player;
	}

	private async Task Fetch( string url )
	{
		byte[] bytes;

		try
		{
			bytes = await Http.RequestBytesAsync( url );
		}
		catch ( Exception e )
		{
			Log.Warning( $"music: could not fetch {url}: {e.Message}" );
			return;
		}

		await GameTask.MainThread();

		if ( disposed ) return;

		Start( bytes );
	}

	private bool disposed;

	//Holds the song and starts it playing.
	private bool Start( ReadOnlySpan<byte> bytes )
	{
		sound = CoreSound.FromMemory( bytes );

		if ( sound is null )
		{
			Log.Warning( $"music: {source} is not a song this can play - wav, mp3, flac or ogg" );
			return false;
		}

		SampleRate = sound.SampleRate;
		Channels = sound.ChannelCount;

		//With nothing to play through, there is nothing to start: a server, a test. The player still says what the song is.
		if ( Application.IsHeadless || Application.IsUnitTest ) return true;

		sampler = new CoreAudioSampler( sound, repeat );

		if ( !sampler.IsValid )
		{
			sampler.Dispose();
			sampler = null;

			Log.Warning( $"music: {source} would not decode" );
			return false;
		}

		//The sampler counts in the mixer's frames, so the handle does too - that is what makes its Time seconds.
		handle = Sound.PlayStream( sampler, (int)AudioEngine.SamplingRate, volume, 1.0f, $"Music: {source}" );

		if ( !handle.IsValid() )
		{
			sampler = null;
			return false;
		}

		handle.ListenLocal = listenLocal;
		handle.Position = position;
		handle.Paused = paused;
		handle.TargetMixer = targetMixer;
		handle.Distance = distance;
		if ( falloff is { } curve ) handle.Falloff = curve;
		if ( lipSync ) handle.LipSync.Enabled = true;

		lock ( playing ) playing.Add( new WeakReference<MusicPlayer>( this ) );

		return true;
	}

	/// <summary>Stops the song. It cannot be started again; play another.</summary>
	public void Stop()
	{
		if ( handle.IsValid() ) handle.Stop();

		//Stopped on purpose, which is not the song finishing.
		finishedSaid = true;
	}

	/// <summary>Moves to a time in the song, in seconds.</summary>
	public void Seek( float time )
	{
		if ( !handle.IsValid() ) return;

		var duration = Duration;
		if ( duration > 0.0f ) time = Math.Clamp( time, 0.0f, duration );

		handle.Time = MathF.Max( time, 0.0f );
	}

	/// <summary>One thing the song's tags say, by name - <c>title</c>, <c>artist</c>, <c>album</c>, <c>date</c>, <c>genre</c> - or null.</summary>
	public string GetMeta( string key )
	{
		if ( sound is null || string.IsNullOrEmpty( key ) ) return null;

		return sound.Tags.TryGetValue( key, out var value ) ? value : null;
	}

	//Every player, once a frame on the main thread: says when a song went round or came to its end.
	internal static void TickAll()
	{
		lock ( playing )
		{
			for ( var i = playing.Count - 1; i >= 0; --i )
			{
				if ( !playing[i].TryGetTarget( out var player ) || player.disposed )
				{
					playing.RemoveAt( i );
					continue;
				}

				player.Tick();
			}
		}
	}

	private void Tick()
	{
		if ( sampler is null ) return;

		var loops = sampler.Loops;

		while ( loopsSeen < loops )
		{
			loopsSeen++;
			OnRepeated?.Invoke();
		}

		if ( !finishedSaid && ( !handle.IsValid() || handle.Finished ) )
		{
			finishedSaid = true;
			OnFinished?.Invoke();
		}
	}

	private ReadOnlySpan<float> GetSpectrum()
	{
		const int Size = CoreAudioSampler.TapLength;

		if ( sampler is null ) return default;

		spectrum ??= new float[Size / 2];
		real ??= new float[Size];
		imaginary ??= new float[Size];
		window ??= Fourier.Hann( Size );

		if ( !sampler.Listen( real ) ) return default;

		for ( var i = 0; i < Size; ++i )
		{
			real[i] *= window[i];
			imaginary[i] = 0.0f;
		}

		Fourier.Transform( real, imaginary );

		//Scaled so a full-scale sine in the middle of a bin reads about one: the transform sums the window, which averages a half.
		var scale = 4.0f / Size;

		for ( var i = 0; i < spectrum.Length; ++i )
			spectrum[i] = MathF.Sqrt( ( real[i] * real[i] ) + ( imaginary[i] * imaginary[i] ) ) * scale;

		return spectrum;
	}

	~MusicPlayer()
	{
		using var finalizing = MainThread.InFinalizer();
		MainThread.QueueDispose( this );
	}

	/// <summary>Stops the song and lets go of it.</summary>
	public void Dispose()
	{
		if ( disposed ) return;

		disposed = true;
		GC.SuppressFinalize( this );

		//The handle owns the sampler from here: disposing it hands the sampler to the mixing thread to be let go of when it is done with it.
		if ( handle.IsValid() ) handle.Dispose();

		handle = default;
		sampler = null;
		sound = null;
	}
}
