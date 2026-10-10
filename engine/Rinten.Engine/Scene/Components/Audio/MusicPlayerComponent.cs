using Rinten.Audio;

namespace Rinten;

/// <summary>Plays a song in the scene: a file from the project, or one from the web - through the whole of it, once or over and over, heard everywhere or from where the object stands. See <see cref="MusicPlayer"/>, which does the playing.</summary>
[Expose]
[Title( "Music Player" )]
[Category( "Audio" )]
[Icon( "music_note" )]
[Tint( EditorTint.Green )]
public sealed class MusicPlayerComponent : Component
{
	/// <summary>The song, from the project's files - wav, mp3, flac or ogg.</summary>
	[Property, FilePath( Extension = "mp3,ogg,wav,flac" )]
	public string Track
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Playing ) Play();
		}
	}

	/// <summary>Or a song from the web, played instead of <see cref="Track"/> when set.</summary>
	[Property]
	public string Url
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			if ( Playing ) Play();
		}
	}

	/// <summary>Starts playing when the scene does.</summary>
	[Property]
	public bool PlayOnStart { get; set; } = true;

	/// <summary>Goes back to the start at the end.</summary>
	[Property]
	public bool Repeat
	{
		get;
		set
		{
			field = value;
			if ( player is not null ) player.Repeat = value;
		}
	} = true;

	[Property, Range( 0, 1 )]
	public float Volume
	{
		get;
		set
		{
			field = value;
			if ( player is not null ) player.Volume = value;
		}
	} = 1.0f;

	/// <summary>The mixer it plays through.</summary>
	[Property]
	public MixerHandle TargetMixer { get; set; }

	/// <summary>Heard from where the object stands, fading with distance, rather than everywhere at the same loudness. Off is background music.</summary>
	[Property, ToggleGroup( "Spatial" )]
	public bool Spatial { get; set; } = false;

	/// <inheritdoc cref="SoundHandle.Distance"/>
	[Property, Group( "Spatial" ), AudioDistanceFloat]
	public float Distance { get; set; } = 15.0f;

	/// <inheritdoc cref="SoundHandle.Falloff"/>
	[Property, Group( "Spatial" )]
	public Curve Falloff { get; set; } = new Curve( new( 0, 1, MathF.PI, -MathF.PI ), new( 1, 0 ) );

	private MusicPlayer player;

	/// <summary>Whether a song is playing, or on its way from the web.</summary>
	public bool Playing => player is not null;

	/// <summary>The song's title, once it has loaded.</summary>
	public string Title => player?.Title;

	/// <summary>How far through the song it is, in seconds.</summary>
	public float PlaybackTime => player?.PlaybackTime ?? 0.0f;

	/// <summary>How long the song is, in seconds.</summary>
	public float Duration => player?.Duration ?? 0.0f;

	/// <summary>Stopped where it is, or carrying on.</summary>
	public bool Paused
	{
		get => player?.Paused ?? false;
		set { if ( player is not null ) player.Paused = value; }
	}

	/// <summary>What is playing now, for a visualiser - see <see cref="MusicPlayer.Spectrum"/>.</summary>
	public ReadOnlySpan<float> Spectrum => player is null ? default : player.Spectrum;

	/// <summary>How loud it is now.</summary>
	public float Amplitude => player?.Amplitude ?? 0.0f;

	/// <summary>Invoked when the song plays to its end.</summary>
	[Property, Group( "Events" )]
	public Action OnFinished { get; set; }

	protected override void OnStart()
	{
		if ( PlayOnStart ) Play();
	}

	protected override void OnDisabled() => Stop();

	protected override void OnDestroy() => Stop();

	protected override void OnUpdate()
	{
		if ( player is null ) return;

		player.TargetMixer = TargetMixer.Get( player.TargetMixer );
		player.ListenLocal = !Spatial;

		if ( Spatial )
		{
			player.Position = WorldPosition;
			player.Distance = Distance;
			player.Falloff = Falloff;
		}
	}

	/// <summary>Plays the song from its start, stopping whatever this was playing.</summary>
	[Button( "Play", "play_arrow" )]
	public void Play()
	{
		Stop();

		player = !string.IsNullOrWhiteSpace( Url )
			? MusicPlayer.PlayUrl( Url )
			: !string.IsNullOrWhiteSpace( Track ) ? MusicPlayer.Play( FileSystem.Mounted, Track ) : null;

		if ( player is null ) return;

		player.Repeat = Repeat;
		player.Volume = Volume;
		player.ListenLocal = !Spatial;
		player.Position = WorldPosition;
		player.OnFinished = Finished;
	}

	/// <summary>Stops the song.</summary>
	[Button( "Stop", "stop" )]
	public void Stop()
	{
		player?.Dispose();
		player = null;
	}

	/// <summary>Moves to a time in the song, in seconds.</summary>
	public void Seek( float time ) => player?.Seek( time );

	private void Finished()
	{
		OnFinished?.Invoke();

		//A song that ended is let go of; a repeating one never ends.
		Stop();
	}
}
