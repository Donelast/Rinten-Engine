using NativeEngine;
using Rinten.Audio;
using System.Diagnostics;
using System.Threading;

namespace Rinten;

/// <summary>Enables video playback and access to the video texture and audio.</summary>
public sealed class VideoPlayer : IDisposable, IWeakInteropHandle
{
	//Every player that has not been disposed, so each is shown once a frame - see TickAll.
	private static readonly List<WeakReference<VideoPlayer>> live = new();

	/// <summary>Video successfully loaded.</summary>
	public Action OnLoaded { get; set; }

	/// <summary>Event that is invoked when the audio stream is created and ready to use.</summary>
	public Action OnAudioReady { get; set; }

	/// <summary>Video finished playing.</summary>
	public Action OnFinished { get; set; }

	/// <summary>Video started playing again after looping.</summary>
	public Action OnRepeated { get; set; }

	public delegate void TextureChangedDelegate( ReadOnlySpan<byte> span, Vector2 size );

	/// <summary>If this event is set, texture data will be provided instead of rendering to the texture.</summary>
	public TextureChangedDelegate OnTextureData { get; set; }

	/// <summary>Sets whether the video should loop when it reaches the end.</summary>
	public bool Repeat
	{
		get => field;
		set
		{
			field = value;
			if ( decoder is not null ) decoder.Repeat = value;
		}
	}

	/// <summary>Gets the total duration of the video in seconds.</summary>
	public float Duration => decoder is { Loaded: true } ? (float)decoder.Duration : 0;

	/// <summary>Gets the current playback time in seconds.</summary>
	public float PlaybackTime => (float)playbackTime;

	/// <summary>Audio sample rate.</summary>
	public int SampleRate { get; private set; }

	/// <summary>Number of audio channels.</summary>
	public int Channels { get; private set; }

	/// <summary>Does the loaded video have audio?</summary>
	public bool HasAudio => decoder is { Loaded: true, HasSound: true };

	/// <summary>Has the video been paused?</summary>
	public bool IsPaused => paused;

	/// <summary>Texture of the video frame.</summary>
	public Texture Texture { get; private set; }

	/// <summary>Width of the video.</summary>
	public int Width => decoder is { Loaded: true } ? decoder.Width : 0;

	/// <summary>Height of the video.</summary>
	public int Height => decoder is { Loaded: true } ? decoder.Height : 0;

	//Overlay stats (overlay_video).
	internal double DecodeSeconds => decoder?.DecodeSeconds ?? 0;
	internal int DecodedFrames => decoder?.DecodedFrames ?? 0;
	internal int PacketQueueDepth => decoder?.PacketQueueDepth ?? 0;
	internal int FrameQueueDepth => decoder?.FrameQueueDepth ?? 0;

	ulong _lastPresentedTick;
	bool _tracksPresentation;

	//Frames since last shown by a panel or any other use; non-panel textures count as used every frame
	internal int LastPresented
	{
		get
		{
			var sinceUsed = Texture is { UsedOutsidePanels: true } texture ? texture.LastUsed : int.MaxValue;

			if ( !_tracksPresentation )
				return Texture?.LastUsed ?? 0;

			var sincePanel = (int)Math.Min( TextureLoader.VideoTextureLoader.Ticks - _lastPresentedTick, 1000UL );
			return Math.Min( sincePanel, sinceUsed );
		}
	}

	internal void TrackPresentation( bool presented )
	{
		_tracksPresentation = true;
		if ( presented ) _lastPresentedTick = TextureLoader.VideoTextureLoader.Ticks;
	}

	uint IWeakInteropHandle.InteropHandle { get; set; }

	private SoundHandle Sound;

	/// <summary>Access audio properties for this video playback.</summary>
	public AudioAccessor Audio { get; internal init; }

	public class AudioAccessor
	{
		private readonly VideoPlayer Player;
		private SoundHandle Sound => Player.Sound;

		private bool listenLocal = true;
		private Vector3 position = Vector3.Forward * 64.0f;
		private float volume = 1.0f;
		private bool lipSync = false;
		private Mixer targetMixer;

		//What GetSpectrum hands out and the scratch its transform works in - kept, so that asking every frame allocates nothing.
		private float[] spectrum;
		private float[] window;
		private float[] real;
		private float[] imaginary;

		internal AudioAccessor( VideoPlayer player )
		{
			Player = player;
		}

		/// <summary>Place the listener at 0,0,0 facing 1,0,0.</summary>
		public bool ListenLocal
		{
			get => listenLocal;
			set
			{
				listenLocal = value;
				if ( Sound.IsValid() && Sound.IsPlaying )
					Sound.ListenLocal = listenLocal;
			}
		}

		/// <summary>Position of the sound.</summary>
		public Vector3 Position
		{
			get => position;
			set
			{
				position = value;
				if ( Sound.IsValid() && Sound.IsPlaying )
					Sound.Position = position;
			}
		}

		/// <summary>Which mixer do we want to write to</summary>
		public Mixer TargetMixer
		{
			get => targetMixer;
			set
			{
				if ( value == targetMixer )
					return;

				targetMixer = value;
				if ( Sound.IsValid() )
					Sound.TargetMixer = targetMixer;
			}
		}

		/// <summary>Volume of the sound.</summary>
		public float Volume
		{
			get => volume;
			set
			{
				if ( value == volume )
					return;

				volume = value;
				if ( Sound.IsValid() )
					Sound.Volume = Player.Muted ? 0.0f : volume;
			}
		}

		/// <summary>Enables lipsync processing.</summary>
		public bool LipSync
		{
			get => lipSync;
			set
			{
				if ( value == lipSync )
					return;

				lipSync = value;
				if ( Sound.IsValid() )
					Sound.LipSync.Enabled = lipSync;
			}
		}

		private float distance = 15_000f;

		public float Distance
		{
			get => distance;
			set
			{
				if ( value == distance )
					return;

				distance = value;
				if ( Sound.IsValid() )
					Sound.Distance = distance;
			}
		}

		private Curve falloff = new( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );

		public Curve Falloff
		{
			get => falloff;
			set
			{
				falloff = value;
				if ( Sound.IsValid() )
					Sound.Falloff = falloff;
			}
		}

		/// <summary>A list of 15 lipsync viseme weights. Requires <see cref="LipSync"/> to be enabled.</summary>
		public IReadOnlyList<float> Visemes => Sound.IsValid() ? Sound.LipSync.Visemes : Array.Empty<float>();

		//How strong each frequency is in what the video is playing now: 512 magnitudes from nothing to
		//half the mixer's rate. Empty for the first frame it is asked for, while it starts listening.
		internal ReadOnlySpan<float> GetSpectrum()
		{
			const int Size = CoreAudioSampler.TapLength;

			if ( Player.sampler is not { } listened ) return default;

			spectrum ??= new float[Size / 2];
			real ??= new float[Size];
			imaginary ??= new float[Size];
			window ??= Fourier.Hann( Size );

			if ( !listened.Listen( real ) ) return default;

			for ( var i = 0; i < Size; ++i )
			{
				real[i] *= window[i];
				imaginary[i] = 0.0f;
			}

			Fourier.Transform( real, imaginary );

			//Scaled as a song's is, so a full-scale sine in a bin reads about one.
			var scale = 4.0f / Size;

			for ( var i = 0; i < spectrum.Length; ++i )
				spectrum[i] = MathF.Sqrt( ( real[i] * real[i] ) + ( imaginary[i] * imaginary[i] ) ) * scale;

			return spectrum;
		}

		//How loud it is now, roughly: the peak of what last went out.
		internal float GetAmplitude() => Sound.IsValid() ? Sound.Amplitude : 0.0f;

		//Everything said to this before there was a sound, said to the one there is now.
		internal void Apply( SoundHandle handle )
		{
			handle.ListenLocal = listenLocal;
			handle.Position = position;
			handle.TargetMixer = targetMixer;
			handle.Volume = Player.Muted ? 0.0f : volume;
			handle.Distance = distance;
			handle.Falloff = falloff;
			if ( lipSync ) handle.LipSync.Enabled = true;
		}
	}

	//Get meta data string.
	internal string GetMeta( string key ) => decoder?.Tag( key );

	private VideoDecoder decoder;
	private StreamSampler sampler;
	private VideoDecoder.SoundTrack playingTrack;
	private string source;
	private bool videoOnly;
	private bool paused;
	private bool disposed;

	//The generation of the decoder's output being shown - bumped by every move.
	private int generation;

	//Whether the clock has started for this generation: it waits for the first picture.
	private bool primed;

	//Where the clock stood when it was last anchored to the wall, and when that was.
	private double wallBase;
	private long wallAnchor;

	//Whether the clock was the sound's the last time it was read, and what it said.
	private bool onSoundClock;
	private double lastClock;

	//What the sound's clock said when it last moved, and when that was - see Clock.
	private double lastSoundClock = double.NaN;
	private long soundMovedAt;
	private bool soundStalled;

	private double playbackTime;
	private int passSeen;
	private bool loadedSaid;
	private bool audioReadySaid;
	private bool finishedSaid;
	private long presentedFrame = -1;

	public VideoPlayer()
	{
		InteropSystem.AllocWeak( this );

		Texture = Texture.Create( 1, 1 )
			.WithName( "video-placeholder" )
			.WithData( new byte[4] { 0, 0, 0, 0 } )
			.Finish();

		Texture.IsLoaded = false;
		Texture.ParentObject = this;

		Audio = new AudioAccessor( this );

		lock ( live ) live.Add( new WeakReference<VideoPlayer>( this ) );
	}

	~VideoPlayer()
	{
		using var finalizing = MainThread.InFinalizer();
		MainThread.QueueDispose( this );
	}

	private void FreeAudio()
	{
		if ( Sound.IsValid() ) Sound.Dispose();

		Sound = null;
		sampler = null;
		playingTrack = null;
	}

	public void Dispose()
	{
		if ( disposed ) return;

		disposed = true;
		GC.SuppressFinalize( this );

		StopDecoding();

		if ( Texture is not null ) Texture.ParentObject = null;
	}

	private void StopDecoding()
	{
		FreeAudio();

		decoder?.Dispose();
		decoder = null;
	}

	/// <summary>Plays a video file from a URL. If there's already a video playing, it will stop.</summary>
	public void Play( string url )
	{
		if ( string.IsNullOrWhiteSpace( url ) ) return;

		if ( Uri.TryCreate( url, UriKind.Absolute, out var uri ) && !uri.IsFile && !Http.IsAllowed( uri ) )
		{
			Log.Warning( $"video: {url} is not an address a game may play from" );
			return;
		}

		if ( !CanPlay() ) return;

		Begin( url, VideoDecoder.FromUrl( url, WantsSound, (int)AudioEngine.SamplingRate ) );
	}

	/// <summary>Plays a video file from a relative path. If there's already a video playing, it will stop.</summary>
	public void Play( BaseFileSystem filesystem, string path )
	{
		if ( filesystem is null || string.IsNullOrWhiteSpace( path ) ) return;

		if ( !filesystem.FileExists( path ) )
		{
			Log.Warning( $"video: {path} is not there" );
			return;
		}

		if ( !CanPlay() ) return;

		System.IO.Stream stream;

		try
		{
			stream = filesystem.OpenRead( path );
		}
		catch ( Exception e )
		{
			Log.Warning( $"video: could not open {path}: {e.Message}" );
			return;
		}

		Begin( path, VideoDecoder.FromStream( stream, path, WantsSound, (int)AudioEngine.SamplingRate ) );
	}

	private bool CanPlay()
	{
		if ( disposed ) return false;

		if ( FFmpeg.IsAvailable ) return true;

		FFmpeg.SayUnavailable();
		return false;
	}

	//Whether this player's sound is decoded at all: not for a picture on a surface, and not where nothing is heard.
	private bool WantsSound => !videoOnly && !Application.IsHeadless && !Application.IsUnitTest;

	private void Begin( string from, VideoDecoder started )
	{
		StopDecoding();

		source = from;
		decoder = started;
		decoder.Repeat = Repeat;

		generation = 0;
		paused = false;
		primed = false;
		onSoundClock = false;
		soundStalled = false;
		lastSoundClock = double.NaN;
		wallBase = 0.0;
		lastClock = 0.0;
		playbackTime = 0.0;
		passSeen = 0;
		loadedSaid = false;
		audioReadySaid = false;
		finishedSaid = false;
	}

	/// <summary>Resumes video playback.</summary>
	public void Resume()
	{
		if ( !paused ) return;

		paused = false;
		wallAnchor = Stopwatch.GetTimestamp();

		if ( Sound.IsValid() ) Sound.Paused = false;
	}

	/// <summary>Stops video playback.</summary>
	public void Stop()
	{
		if ( decoder is null ) return;

		StopDecoding();

		playbackTime = 0.0;
		paused = false;

		//Stopped on purpose, which is not the video finishing.
		finishedSaid = true;
	}

	/// <summary>Pauses video playback.</summary>
	public void Pause()
	{
		if ( paused ) return;

		wallBase = Clock();
		paused = true;

		if ( Sound.IsValid() ) Sound.Paused = true;
	}

	/// <summary>Toggle video playback</summary>
	public void TogglePause()
	{
		if ( paused ) Resume();
		else Pause();
	}

	/// <summary>Sets the playback position to a specified time in the video, given in seconds.</summary>
	public void Seek( float time )
	{
		if ( decoder is null ) return;

		var duration = Duration;
		var target = duration > 0 ? Math.Clamp( time, 0.0f, duration ) : Math.Max( time, 0.0f );

		generation = decoder.Seek( target );

		//The sound of the old generation stops now; the new one's is made once the decoder has it.
		FreeAudio();

		primed = false;
		onSoundClock = false;
		soundStalled = false;
		lastSoundClock = double.NaN;
		wallBase = target;
		lastClock = target;
		playbackTime = target;
		passSeen = 0;
		finishedSaid = false;
	}

	//Every player is presented once a frame by the engine; calling this as well does no harm, and does nothing a second time in the same frame.
	/// <summary>Present a video frame.</summary>
	public void Present()
	{
		if ( disposed || decoder is null ) return;

		ThreadSafe.AssertIsMainThread();

		var frame = EngineLoop.LoopFrames;
		if ( presentedFrame == frame ) return;
		presentedFrame = frame;

		if ( !loadedSaid )
		{
			if ( decoder.Failure is not null && !decoder.Loaded )
			{
				//Said by the decoder as it failed; the player just stops.
				StopDecoding();
				return;
			}

			if ( !decoder.Loaded ) return;

			loadedSaid = true;
			Channels = decoder.SoundChannels;
			SampleRate = decoder.HasSound ? (int)AudioEngine.SamplingRate : 0;

			OnLoaded?.Invoke();
		}

		UpdateSound();

		var clock = Clock();

		var picture = decoder.Take( clock, generation );

		if ( picture is not null )
		{
			Show( picture );
			VideoDecoder.Recycle( picture );
		}

		var (start, pass) = decoder.PassStart( clock );

		while ( passSeen < pass )
		{
			passSeen++;
			OnRepeated?.Invoke();
		}

		playbackTime = Math.Max( clock - start, 0.0 );

		if ( !finishedSaid && decoder.HasEnded( generation ) && !decoder.HasFrame( generation ) &&
			( playingTrack is null || playingTrack.Ended || !Sound.IsValid() || soundStalled ) )
		{
			finishedSaid = true;
			OnFinished?.Invoke();
		}
	}

	//Where the video is, on the decoder's timeline: the sound as the mixer has played it, where there is sound
	//and it is being heard, and the wall otherwise - from where the sound left it, if it was the sound until now.
	private double Clock()
	{
		if ( decoder is null ) return lastClock;

		//Not started until there is something to start with: a clock that ran while the first picture was being decoded would begin by dropping it.
		if ( !primed )
		{
			var track = decoder.Sound;
			var hasSound = track is not null && track.Generation == generation && Interlocked.Read( ref track.Written ) > 0;

			if ( !decoder.HasFrame( generation ) && !hasSound && !decoder.HasEnded( generation ) ) return wallBase;

			primed = true;
			wallAnchor = Stopwatch.GetTimestamp();
		}

		var playing = playingTrack;

		if ( playing is not null && playing.Generation == generation && Sound.IsValid() && !Sound.Finished && !playing.Ended && !soundStalled )
		{
			var heard = playing.Clock;

			//A sound nobody is playing - no device, a mixer that has stopped - is a clock that never moves, and a
			//video that waits on it never moves either. Given half a second, it is let go of and the wall takes over.
			if ( heard != lastSoundClock || paused || playing.Buffer.Queued == 0 )
			{
				lastSoundClock = heard;
				soundMovedAt = Stopwatch.GetTimestamp();
			}
			else if ( Stopwatch.GetElapsedTime( soundMovedAt ).TotalSeconds > 0.5 )
			{
				soundStalled = true;
			}

			if ( !soundStalled )
			{
				lastClock = heard;
				onSoundClock = true;

				return lastClock;
			}
		}

		if ( onSoundClock )
		{
			onSoundClock = false;
			wallBase = lastClock;
			wallAnchor = Stopwatch.GetTimestamp();
		}

		if ( paused ) return wallBase;

		lastClock = wallBase + Stopwatch.GetElapsedTime( wallAnchor ).TotalSeconds;
		return lastClock;
	}

	//Makes the sound for the generation being shown, once the decoder has one.
	private void UpdateSound()
	{
		if ( !WantsSound ) return;

		var track = decoder.Sound;

		if ( track is null || track.Generation != generation || ReferenceEquals( track, playingTrack ) ) return;

		FreeAudio();

		sampler = new StreamSampler( track.Buffer );

		//The track is written at the mixer's own rate, so the handle counts in the mixer's frames and its Time is seconds.
		var handle = Rinten.Sound.PlayStream( sampler, (int)AudioEngine.SamplingRate, Audio.Volume, 1.0f, $"Video: {source}" );

		if ( !handle.IsValid() )
		{
			sampler = null;
			return;
		}

		Sound = handle;
		playingTrack = track;

		Audio.Apply( handle );
		handle.Paused = paused;

		if ( !audioReadySaid )
		{
			audioReadySaid = true;
			OnAudioReady?.Invoke();
		}
	}

	//A decoded picture into the texture, or handed to whoever asked for the pixels instead.
	private void Show( VideoDecoder.Frame picture )
	{
		var bytes = picture.Width * picture.Height * 4;
		var pixels = new ReadOnlySpan<byte>( picture.Pixels, 0, bytes );

		if ( OnTextureData is { } handOver )
		{
			handOver( pixels, new Vector2( picture.Width, picture.Height ) );
			return;
		}

		if ( Texture is null ) return;

		if ( Texture.Width != picture.Width || Texture.Height != picture.Height )
		{
			//Made anew at the new size and taken into the texture everyone already holds - a material or a
			//panel that was handed this texture before the video had loaded keeps it and sees the video in it.
			var made = Texture.Create( picture.Width, picture.Height, ImageFormat.RGBA8888 )
				.WithName( "video" )
				.Finish();

			if ( !Texture.CopyFrom( made ) )
			{
				made.Dispose();
				return;
			}
		}

		Texture.Update( pixels );
		Texture.IsLoaded = true;
	}

	/// <summary>The video is muted</summary>
	public bool Muted
	{
		get => field;
		set
		{
			field = value;
			if ( Sound.IsValid() ) Sound.Volume = value ? 0.0f : Audio.Volume;
		}
	}

	//A picture and nothing else - a video on a surface, which is seen and not heard. Its sound is not decoded at all.
	internal void SetVideoOnly()
	{
		videoOnly = true;
	}

	//Every player, once a frame on the main thread: the picture that is due shown, and a loop or an end said.
	internal static void TickAll()
	{
		lock ( live )
		{
			for ( var i = live.Count - 1; i >= 0; --i )
			{
				if ( !live[i].TryGetTarget( out var player ) || player.disposed )
				{
					live.RemoveAt( i );
					continue;
				}

				player.Present();
			}
		}
	}
}
