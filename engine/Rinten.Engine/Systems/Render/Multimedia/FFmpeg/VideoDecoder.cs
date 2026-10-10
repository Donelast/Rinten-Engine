using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Rinten.Audio;
using static Rinten.FFmpeg;

namespace Rinten;

//Decodes one video on its own thread into a picture queue and audio stream; the player paces it
//Times are on a timeline across loops; Seek starts a new generation so stale frames are dropped
internal sealed unsafe class VideoDecoder : IDisposable
{
	//One decoded picture, RGBA, and when on the timeline it is due.
	internal sealed class Frame
	{
		public byte[] Pixels;
		public int Width;
		public int Height;
		public double Time;
		public int Generation;
	}

	//The sound of one generation: the stream it is written into, where on the timeline its first sample
	//falls and how many frames of it have been written. How many of those the mixer has taken is its clock.
	internal sealed class SoundTrack
	{
		public StreamBuffer Buffer;
		public int Generation;
		public double Start;
		public int Rate;
		public int Channels;
		public long Written;

		/// <summary>Where on the timeline what is being heard now is.</summary>
		public double Clock => Start + ( Interlocked.Read( ref Written ) - Buffer.Queued ) / (double)Rate;

		/// <summary>The decoder will write no more, and the mixer has played what it did.</summary>
		public bool Ended => Buffer.Closed && Buffer.Queued == 0;
	}

	static readonly Logger log = new( "Video" );

	readonly Stream input;
	readonly string url;
	readonly string name;
	readonly bool wantsSound;
	readonly int soundRate;

	GCHandle self;
	Thread thread;

	volatile bool stopping;
	readonly AutoResetEvent wake = new( false );

	/// <summary>Goes back to the start at the end, rather than ending. Read by the decoder each time it reaches the end.</summary>
	public volatile bool Repeat;

	readonly object gate = new();

	//Asked for by the player and done by the decoder, under the gate.
	int wantedGeneration;
	double wantedTime;

	readonly List<Frame> frames = new();
	readonly List<double> passStarts = new() { 0.0 };
	int endedGeneration = -1;

	volatile SoundTrack sound;

	/// <summary>Opened, and <see cref="Width"/>, <see cref="Duration"/> and the rest are known.</summary>
	public volatile bool Loaded;

	/// <summary>Why it would not play, once it has not.</summary>
	public volatile string Failure;

	public int Width { get; private set; }
	public int Height { get; private set; }
	public double Duration { get; private set; }
	public bool HasVideo { get; private set; }
	public bool HasSound { get; private set; }
	public int SoundChannels { get; private set; }

	//Overlay stats (overlay_video). Monotonic totals - the reader makes a rate over its own
	//window - plus the queue depths that say whether the decoder keeps up or is starved.
	long busyTicks;
	int decodedFrames;

	/// <summary>Wall time the decoder's thread has spent working rather than waiting, in seconds.</summary>
	public double DecodeSeconds => Interlocked.Read( ref busyTicks ) / (double)System.Diagnostics.Stopwatch.Frequency;

	/// <summary>Pictures received from the codec so far.</summary>
	public int DecodedFrames => Volatile.Read( ref decodedFrames );

	/// <summary>Video packets read and waiting for the codec. Read racily off the decoder's thread; for the overlay only.</summary>
	public int PacketQueueDepth => waitingVideo.Count;

	/// <summary>Pictures decoded and waiting to be shown.</summary>
	public int FrameQueueDepth => QueuedFrames();

	/// <summary>How many pictures may wait decoded - fewer, the larger they are.</summary>
	public int MaxFrames { get; private set; } = 4;

	//What the file's tags said, read once as it opened.
	readonly Dictionary<string, string> tags = new( StringComparer.OrdinalIgnoreCase );

	//FFmpeg's side, touched by the decoder's thread alone.
	void* format;
	void* io;
	void* videoCodec;
	void* soundCodec;
	void* packet;
	void* decoded;
	void* scaled;
	void* scaler;
	void* resampler;
	int videoIndex = -1;
	int soundIndex = -1;
	double videoTimeBase;
	double soundTimeBase;
	double frameLength;
	double mediaStart;

	int resampleRate;
	int resampleFormat;
	int resampleChannels;
	float[] soundScratch = Array.Empty<float>();

	readonly Queue<nint> waitingVideo = new();
	bool endOfFile;
	bool videoReceiving;
	bool videoFlushed;
	bool videoDone;
	bool soundFlushed;
	int handledGeneration;
	double passOffset;
	double passEnd;
	double skipBefore = double.NegativeInfinity;
	double lastVideoTime;

	VideoDecoder( Stream input, string url, string name, bool wantsSound, int soundRate )
	{
		this.input = input;
		this.url = url;
		this.name = name;
		this.wantsSound = wantsSound;
		this.soundRate = soundRate;
	}

	/// <summary>Starts decoding a file the game opened - a stream it may read and, where it can, seek.</summary>
	public static VideoDecoder FromStream( Stream stream, string name, bool wantsSound, int soundRate )
	{
		var decoder = new VideoDecoder( stream, null, name, wantsSound, soundRate );
		decoder.Start();
		return decoder;
	}

	/// <summary>Starts decoding what is at an address - a web one, or a file FFmpeg opens itself.</summary>
	public static VideoDecoder FromUrl( string url, bool wantsSound, int soundRate )
	{
		var decoder = new VideoDecoder( null, url, url, wantsSound, soundRate );
		decoder.Start();
		return decoder;
	}

	void Start()
	{
		self = GCHandle.Alloc( this );

		thread = new Thread( Run )
		{
			IsBackground = true,
			Name = "Video decode",
		};

		thread.Start();
	}

	/// <summary>The sound of the generation being decoded, or null where there is no sound.</summary>
	public SoundTrack Sound => sound;

	/// <summary>The generation the player last asked for.</summary>
	public int Generation
	{
		get
		{
			lock ( gate ) return wantedGeneration;
		}
	}

	/// <summary>Moves to a time on the file's own clock, in seconds; the new generation.</summary>
	public int Seek( double time )
	{
		int generation;

		lock ( gate )
		{
			generation = ++wantedGeneration;
			wantedTime = Math.Max( time, 0.0 );

			foreach ( var frame in frames ) Recycle( frame );
			frames.Clear();
		}

		wake.Set();
		return generation;
	}

	/// <summary>Whether the decoder has reached the end of this generation and will write no more of it.</summary>
	public bool HasEnded( int generation )
	{
		lock ( gate ) return endedGeneration == generation;
	}

	/// <summary>Where on the timeline the pass that <paramref name="time"/> falls in began, and which pass it is.</summary>
	public (double Start, int Pass) PassStart( double time )
	{
		lock ( gate )
		{
			var pass = 0;

			for ( var i = 1; i < passStarts.Count; i++ )
			{
				if ( passStarts[i] <= time ) pass = i;
			}

			return (passStarts[pass], pass);
		}
	}

	/// <summary>Whether a picture of this generation is waiting.</summary>
	public bool HasFrame( int generation )
	{
		lock ( gate ) return frames.Any( frame => frame.Generation == generation );
	}

	/// <summary>The newest picture due by <paramref name="clock"/>, or null where none is. Those it passed over are dropped: they were late. Hand what comes back to <see cref="Recycle"/> once its pixels have been used.</summary>
	public Frame Take( double clock, int generation )
	{
		Frame due = null;

		lock ( gate )
		{
			while ( frames.Count > 0 )
			{
				var next = frames[0];

				if ( next.Generation == generation && next.Time > clock ) break;

				frames.RemoveAt( 0 );

				if ( next.Generation != generation ) { Recycle( next ); continue; }

				if ( due is not null ) Recycle( due );
				due = next;
			}
		}

		if ( due is not null ) wake.Set();

		return due;
	}

	/// <summary>Gives a picture's memory back.</summary>
	public static void Recycle( Frame frame )
	{
		if ( frame?.Pixels is null ) return;

		ArrayPool<byte>.Shared.Return( frame.Pixels );
		frame.Pixels = null;
	}

	/// <summary>One of the file's tags - <c>title</c>, <c>artist</c> - or null.</summary>
	public string Tag( string key )
	{
		if ( string.IsNullOrEmpty( key ) ) return null;

		lock ( tags ) return tags.TryGetValue( key, out var value ) ? value : null;
	}

	public void Dispose()
	{
		//The thread lets go of everything itself as it finishes, which it does at the next thing it waits on - an address that is still
		//being fetched is told to give up. Not waited for here: a network that will not answer is not something the main thread stops for.
		stopping = true;
		wake.Set();
	}

	void Run()
	{
		try
		{
			if ( !Open() ) return;

			Loaded = true;

			while ( !stopping )
			{
				var started = System.Diagnostics.Stopwatch.GetTimestamp();

				if ( !Step() )
				{
					wake.WaitOne( 5 );
					continue;
				}

				Interlocked.Add( ref busyTicks, System.Diagnostics.Stopwatch.GetTimestamp() - started );
			}
		}
		catch ( Exception e )
		{
			Failure ??= e.Message;
			log.Warning( $"{name}: {e.Message}" );
		}
		finally
		{
			Close();
		}
	}

	bool Open()
	{
		format = avformat_alloc_context();
		if ( format is null ) return Fail( "FFmpeg would not make a context" );

		//Asked by FFmpeg whenever it waits on something: an address that does not answer is let go of when the player is.
		SetPointer( format, Offsets.FormatInterruptCallback, (void*)(delegate* unmanaged[Cdecl]<void*, int>)&Interrupt );
		SetPointer( format, Offsets.FormatInterruptCallback + 8, (void*)GCHandle.ToIntPtr( self ) );

		if ( input is not null )
		{
			const int Size = 1 << 16;

			var buffer = (byte*)av_malloc( Size );
			if ( buffer is null ) return Fail( "no memory for reading" );

			io = avio_alloc_context( buffer, Size, 0, (void*)GCHandle.ToIntPtr( self ),
				(nint)(delegate* unmanaged[Cdecl]<void*, byte*, int, int>)&ReadPacket, 0,
				input.CanSeek ? (nint)(delegate* unmanaged[Cdecl]<void*, long, int, long>)&SeekStream : 0 );

			if ( io is null )
			{
				av_freep( &buffer );
				return Fail( "FFmpeg would not read from the file" );
			}

			SetPointer( format, Offsets.FormatPb, io );
		}

		void* options = null;

		if ( url is not null )
		{
			//A web address that stops answering halfway is given fifteen seconds and then taken as ended, rather than holding the thread for ever.
			fixed ( byte* key = Text( "rw_timeout" ) )
			fixed ( byte* value = Text( "15000000" ) )
				av_dict_set( &options, key, value, 0 );
		}

		var context = format;
		int result;

		fixed ( byte* address = Text( url ?? "" ) )
			result = avformat_open_input( &context, address, null, &options );

		av_dict_free( &options );

		if ( result < 0 )
		{
			//Freed by FFmpeg on failure, the context and not the reader.
			format = null;
			return Fail( Error( result ) );
		}

		result = avformat_find_stream_info( format, null );
		if ( result < 0 ) return Fail( Error( result ) );

		ReadTags( Pointer( format, Offsets.FormatMetadata ) );

		var start = Long( format, Offsets.FormatStartTime );
		mediaStart = start == AV_NOPTS_VALUE ? 0.0 : start / 1_000_000.0;

		var duration = Long( format, Offsets.FormatDuration );
		Duration = duration == AV_NOPTS_VALUE || duration <= 0 ? 0.0 : duration / 1_000_000.0;

		void* codec = null;
		videoIndex = av_find_best_stream( format, AVMEDIA_TYPE_VIDEO, -1, -1, &codec, 0 );

		if ( videoIndex >= 0 )
		{
			var stream = StreamAt( videoIndex );

			videoCodec = OpenCodec( stream, codec, threads: true );
			if ( videoCodec is null ) return Fail( "no decoder for its pictures" );

			var parameters = Pointer( stream, Offsets.StreamCodecParameters );

			Width = Int( parameters, Offsets.ParametersWidth );
			Height = Int( parameters, Offsets.ParametersHeight );
			videoTimeBase = Rational( stream, Offsets.StreamTimeBase );

			var rate = Rational( stream, Offsets.StreamAverageFrameRate );
			frameLength = rate > 0.0 ? 1.0 / rate : 1.0 / 30.0;

			//Kept to about sixty-four megabytes of pictures, and never fewer than two - one shown and one on its way.
			var bytes = Math.Max( Width * Height * 4, 1 );
			MaxFrames = Math.Clamp( ( 64 << 20 ) / bytes, 2, 8 );

			HasVideo = true;

			if ( Duration <= 0.0 )
			{
				var length = Long( stream, Offsets.StreamDuration );
				if ( length != AV_NOPTS_VALUE && length > 0 ) Duration = length * videoTimeBase;
			}
		}

		if ( wantsSound )
		{
			codec = null;
			soundIndex = av_find_best_stream( format, AVMEDIA_TYPE_AUDIO, -1, videoIndex, &codec, 0 );

			if ( soundIndex >= 0 )
			{
				var stream = StreamAt( soundIndex );

				soundCodec = OpenCodec( stream, codec, threads: false );

				if ( soundCodec is null )
				{
					log.Warning( $"{name}: no decoder for its sound - it plays without" );
					soundIndex = -1;
				}
				else
				{
					var parameters = Pointer( stream, Offsets.StreamCodecParameters );
					var channels = Int( parameters, Offsets.ParametersChannelLayout + Offsets.ChannelLayoutChannels );

					//Mono stays mono and anything wider is folded to two: the mixer places a sound
					//by its channels, and six of them is a surround mix that has nowhere to go here.
					SoundChannels = channels <= 1 ? 1 : 2;
					soundTimeBase = Rational( stream, Offsets.StreamTimeBase );
					HasSound = true;

					sound = NewTrack( 0, 0.0 );
				}
			}
		}

		if ( !HasVideo && !HasSound ) return Fail( "there is nothing in it to play" );

		packet = av_packet_alloc();
		decoded = av_frame_alloc();
		scaled = av_frame_alloc();

		if ( packet is null || decoded is null || scaled is null ) return Fail( "no memory for decoding" );

		return true;
	}

	bool Fail( string why )
	{
		Failure = why;

		//Let go of before it opened - an address still being fetched when its player went away - is not a video that would not play.
		if ( !stopping ) log.Warning( $"{name} will not play: {why}" );

		return false;
	}

	void* StreamAt( int index ) => ( (void**)Pointer( format, Offsets.FormatStreams ) )[index];

	static void* OpenCodec( void* stream, void* codec, bool threads )
	{
		if ( codec is null ) return null;

		var context = avcodec_alloc_context3( codec );
		if ( context is null ) return null;

		if ( avcodec_parameters_to_context( context, Pointer( stream, Offsets.StreamCodecParameters ) ) < 0 )
		{
			avcodec_free_context( &context );
			return null;
		}

		//As many threads as FFmpeg thinks the machine has, for pictures: a large video on one thread is a video that cannot keep up.
		if ( threads ) SetInt( context, Offsets.CodecThreadCount, 0 );

		if ( avcodec_open2( context, codec, null ) < 0 )
		{
			avcodec_free_context( &context );
			return null;
		}

		return context;
	}

	void ReadTags( void* dictionary )
	{
		if ( dictionary is null ) return;

		void* entry = null;

		//An empty key with IGNORE_SUFFIX matches every entry, one after another.
		fixed ( byte* any = Text( "" ) )
		{
			lock ( tags )
			{
				while ( ( entry = av_dict_get( dictionary, any, entry, 2 ) ) is not null )
				{
					var key = Marshal.PtrToStringUTF8( (nint)Pointer( entry, 0 ) );
					var value = Marshal.PtrToStringUTF8( (nint)Pointer( entry, Offsets.DictionaryEntryValue ) );

					if ( !string.IsNullOrEmpty( key ) ) tags[key] = value;
				}
			}
		}
	}

	SoundTrack NewTrack( int generation, double start )
	{
		return new SoundTrack
		{
			Buffer = new StreamBuffer( soundRate, SoundChannels ),
			Generation = generation,
			Start = start,
			Rate = soundRate,
			Channels = SoundChannels,
		};
	}

	//One piece of work, where there is any: a picture received, a packet sent, a packet
	//read, the end handled. False where there was nothing to do and the thread may wait.
	bool Step()
	{
		int generation;
		double target;

		lock ( gate )
		{
			generation = wantedGeneration;
			target = wantedTime;
		}

		if ( generation != handledGeneration )
		{
			MoveTo( generation, target );
			return true;
		}

		if ( videoCodec is not null && !videoDone )
		{
			if ( videoReceiving )
			{
				if ( QueuedFrames() < MaxFrames )
				{
					ReceiveVideo( generation );
					return true;
				}
			}
			else if ( waitingVideo.Count > 0 )
			{
				var next = (void*)waitingVideo.Peek();
				var sent = avcodec_send_packet( videoCodec, next );

				//Full: what it has has to come out first, and this goes after.
				if ( sent != AVERROR_EAGAIN )
				{
					waitingVideo.Dequeue();
					av_packet_free( &next );
				}

				videoReceiving = true;
				return true;
			}
			else if ( endOfFile && !videoFlushed )
			{
				avcodec_send_packet( videoCodec, null );
				videoFlushed = true;
				videoReceiving = true;
				return true;
			}
		}

		var track = sound;
		var soundOn = soundCodec is not null && track is not null;

		var wantsVideo = videoCodec is not null && !videoDone && !videoReceiving && waitingVideo.Count == 0;
		var wantsSound = soundOn && track.Buffer.Queued < soundRate;

		if ( !endOfFile && ( wantsVideo || wantsSound ) && waitingVideo.Count < 2048 )
		{
			ReadOne( generation );
			return true;
		}

		if ( endOfFile && soundOn && !soundFlushed )
		{
			avcodec_send_packet( soundCodec, null );
			while ( avcodec_receive_frame( soundCodec, decoded ) >= 0 ) WriteSound( track );
			soundFlushed = true;
			return true;
		}

		var finished = endOfFile && ( videoCodec is null || videoDone ) && waitingVideo.Count == 0;

		if ( finished && endedGeneration != generation )
		{
			if ( Repeat && GoRound() ) return true;

			lock ( gate ) endedGeneration = generation;

			track?.Buffer.Close();
			return true;
		}

		return false;
	}

	int QueuedFrames()
	{
		lock ( gate ) return frames.Count;
	}

	void ReadOne( int generation )
	{
		var read = av_read_frame( format, packet );

		if ( read < 0 )
		{
			//The end, or a network that stopped answering, which from here is the same thing.
			endOfFile = true;
			return;
		}

		var index = Int( packet, Offsets.PacketStreamIndex );

		if ( index == videoIndex && videoCodec is not null )
		{
			//Kept rather than decoded now: the pictures may be as far ahead as they are
			//allowed while the sound still wants packets that are behind these in the file.
			var kept = av_packet_alloc();
			if ( kept is null ) { av_packet_unref( packet ); return; }

			var swap = packet;
			packet = kept;
			waitingVideo.Enqueue( (nint)swap );
			return;
		}

		if ( index == soundIndex && soundCodec is not null && sound is { } track )
		{
			if ( avcodec_send_packet( soundCodec, packet ) >= 0 )
			{
				while ( avcodec_receive_frame( soundCodec, decoded ) >= 0 ) WriteSound( track );
			}
		}

		av_packet_unref( packet );
	}

	void ReceiveVideo( int generation )
	{
		var result = avcodec_receive_frame( videoCodec, decoded );

		if ( result == AVERROR_EAGAIN )
		{
			videoReceiving = false;
			return;
		}

		if ( result < 0 )
		{
			//The end once it was flushed, or a stream it cannot go on with.
			videoDone = true;
			videoReceiving = false;
			return;
		}

		Interlocked.Increment( ref decodedFrames );

		EmitPicture( generation );
		av_frame_unref( decoded );
	}

	void EmitPicture( int generation )
	{
		var stamp = Long( decoded, Offsets.FrameBestEffortTimestamp );
		if ( stamp == AV_NOPTS_VALUE ) stamp = Long( decoded, Offsets.FramePts );

		var media = stamp == AV_NOPTS_VALUE ? lastVideoTime + frameLength : stamp * videoTimeBase;
		lastVideoTime = media;

		var length = Long( decoded, Offsets.FrameDuration );
		var seconds = length > 0 ? length * videoTimeBase : frameLength;

		passEnd = Math.Max( passEnd, media + seconds - mediaStart );

		var time = passOffset + media - mediaStart;

		//Decoded on the way to where it was moved to, from the key frame before it: not shown.
		if ( time + seconds <= skipBefore + 1e-6 ) return;

		var width = Int( decoded, Offsets.FrameWidth );
		var height = Int( decoded, Offsets.FrameHeight );

		if ( width <= 0 || height <= 0 ) return;

		var pixels = ArrayPool<byte>.Shared.Rent( width * height * 4 );

		if ( !Convert( pixels, width, height ) )
		{
			ArrayPool<byte>.Shared.Return( pixels );
			return;
		}

		lock ( gate )
		{
			if ( generation != wantedGeneration )
			{
				ArrayPool<byte>.Shared.Return( pixels );
				return;
			}

			frames.Add( new Frame { Pixels = pixels, Width = width, Height = height, Time = time, Generation = generation } );
		}
	}

	//The decoded picture into RGBA, whatever it was decoded as.
	bool Convert( byte[] pixels, int width, int height )
	{
		if ( scaler is null )
		{
			scaler = sws_alloc_context();
			if ( scaler is null ) return false;

			SetInt( scaler, Offsets.ScaleFlags, SWS_BILINEAR );
		}

		fixed ( byte* into = pixels )
		{
			SetPointer( scaled, Offsets.FrameData, into );
			SetInt( scaled, Offsets.FrameLineSize, width * 4 );
			SetInt( scaled, Offsets.FrameWidth, width );
			SetInt( scaled, Offsets.FrameHeight, height );
			SetInt( scaled, Offsets.FrameFormat, AV_PIX_FMT_RGBA );

			var result = sws_scale_frame( scaler, scaled, decoded );

			var wrote = Pointer( scaled, Offsets.FrameData );

			//Where the scaler chose to make its own picture after all, it is copied across - and let go of, which is FFmpeg's to do.
			if ( result >= 0 && wrote != into && wrote is not null )
			{
				var stride = Int( scaled, Offsets.FrameLineSize );

				for ( var row = 0; row < height; row++ )
					Buffer.MemoryCopy( (byte*)wrote + ( row * stride ), into + ( row * width * 4 ), width * 4, width * 4 );
			}

			SetPointer( scaled, Offsets.FrameData, null );
			av_frame_unref( scaled );

			return result >= 0;
		}
	}

	//The decoded sound into the track: resampled to the mixer's rate as floats, with a gap in the file
	//written as silence and an overlap left out, so the samples written stay a clock of the timeline.
	void WriteSound( SoundTrack track )
	{
		var count = Int( decoded, Offsets.FrameSampleCount );
		var rate = Int( decoded, Offsets.FrameSampleRate );
		var sampleFormat = Int( decoded, Offsets.FrameFormat );
		var layout = (byte*)decoded + Offsets.FrameChannelLayout;
		var channels = Int( layout, Offsets.ChannelLayoutChannels );

		if ( count <= 0 || rate <= 0 || channels <= 0 ) return;

		if ( resampler is null || resampleRate != rate || resampleFormat != sampleFormat || resampleChannels != channels )
		{
			if ( resampler is not null )
			{
				var old = resampler;
				swr_free( &old );
				resampler = null;
			}

			var wanted = stackalloc byte[Offsets.ChannelLayoutSize];
			new Span<byte>( wanted, Offsets.ChannelLayoutSize ).Clear();
			av_channel_layout_default( wanted, track.Channels );

			void* made = null;
			var result = swr_alloc_set_opts2( &made, wanted, AV_SAMPLE_FMT_FLT, track.Rate, layout, sampleFormat, rate, 0, null );

			av_channel_layout_uninit( wanted );

			if ( result < 0 || made is null || swr_init( made ) < 0 )
			{
				if ( made is not null ) swr_free( &made );
				return;
			}

			resampler = made;
			resampleRate = rate;
			resampleFormat = sampleFormat;
			resampleChannels = channels;
		}

		var room = swr_get_out_samples( resampler, count );
		if ( room <= 0 ) return;

		if ( soundScratch.Length < room * track.Channels ) soundScratch = new float[room * track.Channels];

		int converted;

		fixed ( float* scratch = soundScratch )
		{
			var output = (byte*)scratch;
			converted = swr_convert( resampler, &output, room, (byte**)Pointer( decoded, Offsets.FrameExtendedData ), count );
		}

		if ( converted <= 0 ) return;

		var samples = new ReadOnlySpan<float>( soundScratch, 0, converted * track.Channels );

		var stamp = Long( decoded, Offsets.FrameBestEffortTimestamp );
		if ( stamp == AV_NOPTS_VALUE ) stamp = Long( decoded, Offsets.FramePts );

		if ( stamp != AV_NOPTS_VALUE )
		{
			var media = stamp * soundTimeBase;
			var time = passOffset + media - mediaStart;

			passEnd = Math.Max( passEnd, media + ( count / (double)rate ) - mediaStart );

			var expected = track.Start + ( Interlocked.Read( ref track.Written ) / (double)track.Rate );
			var gap = time - expected;

			//Thirty milliseconds either way is jitter in the file's stamps and is left alone; more is a hole, or a start before where it was moved to.
			if ( gap > 0.03 )
			{
				var silence = (int)( Math.Min( gap, 2.0 ) * track.Rate ) * track.Channels;
				Span<float> quiet = stackalloc float[1024];
				quiet.Clear();

				while ( silence > 0 )
				{
					var piece = Math.Min( silence, quiet.Length - ( quiet.Length % track.Channels ) );
					Interlocked.Add( ref track.Written, track.Buffer.Write( quiet[..piece] ) );
					silence -= piece;
				}
			}
			else if ( gap < -0.03 )
			{
				var drop = Math.Min( (int)( -gap * track.Rate ), converted );
				samples = samples[( drop * track.Channels )..];
			}
		}

		if ( samples.Length > 0 ) Interlocked.Add( ref track.Written, track.Buffer.Write( samples ) );
	}

	//Back to the start, for a video that repeats; false where there is nothing to go round.
	bool GoRound()
	{
		var length = passEnd > 0.0 ? passEnd : Duration;
		if ( length <= 0.0 ) return false;

		if ( av_seek_frame( format, -1, (long)( mediaStart * 1_000_000.0 ), AVSEEK_FLAG_BACKWARD ) < 0 ) return false;

		Restart();

		passOffset += length;
		passEnd = 0.0;
		skipBefore = double.NegativeInfinity;

		lock ( gate ) passStarts.Add( passOffset );

		return true;
	}

	//Moves to a time asked for, as a new generation.
	void MoveTo( int generation, double target )
	{
		handledGeneration = generation;

		var stamp = (long)( ( target + mediaStart ) * 1_000_000.0 );

		if ( av_seek_frame( format, -1, stamp, AVSEEK_FLAG_BACKWARD ) < 0 )
			av_seek_frame( format, -1, stamp, 0 );

		Restart();

		passOffset = 0.0;
		passEnd = 0.0;
		skipBefore = target;
		lastVideoTime = target;

		var old = sound;

		if ( old is not null )
		{
			old.Buffer.Close();
			sound = NewTrack( generation, target );
		}

		lock ( gate )
		{
			passStarts.Clear();
			passStarts.Add( 0.0 );

			if ( endedGeneration != generation ) endedGeneration = -1;
		}
	}

	//Forgets where it was: decoders emptied, nothing waiting, the end not reached.
	void Restart()
	{
		if ( videoCodec is not null ) avcodec_flush_buffers( videoCodec );
		if ( soundCodec is not null ) avcodec_flush_buffers( soundCodec );

		while ( waitingVideo.Count > 0 )
		{
			var waiting = (void*)waitingVideo.Dequeue();
			av_packet_free( &waiting );
		}

		endOfFile = false;
		videoReceiving = false;
		videoFlushed = false;
		videoDone = false;
		soundFlushed = false;
	}

	void Close()
	{
		while ( waitingVideo.Count > 0 )
		{
			var waiting = (void*)waitingVideo.Dequeue();
			av_packet_free( &waiting );
		}

		if ( scaler is not null ) { var s = scaler; sws_free_context( &s ); scaler = null; }
		if ( resampler is not null ) { var r = resampler; swr_free( &r ); resampler = null; }
		if ( decoded is not null ) { var f = decoded; av_frame_free( &f ); decoded = null; }
		if ( scaled is not null ) { var f = scaled; av_frame_free( &f ); scaled = null; }
		if ( packet is not null ) { var p = packet; av_packet_free( &p ); packet = null; }
		if ( videoCodec is not null ) { var c = videoCodec; avcodec_free_context( &c ); videoCodec = null; }
		if ( soundCodec is not null ) { var c = soundCodec; avcodec_free_context( &c ); soundCodec = null; }
		if ( format is not null ) { var f = format; avformat_close_input( &f ); format = null; }

		//A reader of ours is not FFmpeg's to free: its buffer - which FFmpeg may have swapped for a larger one - and then the reader.
		if ( io is not null )
		{
			av_freep( (byte*)io + Offsets.IoBuffer );

			var reader = io;
			avio_context_free( &reader );
			io = null;
		}

		try
		{
			input?.Dispose();
		}
		catch ( Exception )
		{
			//Closing a file that is already gone.
		}

		lock ( gate )
		{
			foreach ( var frame in frames ) Recycle( frame );
			frames.Clear();

			//Nothing more is coming: a player still waiting on the end sees it.
			endedGeneration = wantedGeneration;
		}

		sound?.Buffer.Close();

		if ( self.IsAllocated ) self.Free();
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static int Interrupt( void* opaque )
	{
		var decoder = GCHandle.FromIntPtr( (nint)opaque ).Target as VideoDecoder;
		return decoder is null || decoder.stopping ? 1 : 0;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static int ReadPacket( void* opaque, byte* buffer, int size )
	{
		try
		{
			var decoder = (VideoDecoder)GCHandle.FromIntPtr( (nint)opaque ).Target;
			var read = decoder.input.Read( new Span<byte>( buffer, size ) );

			return read > 0 ? read : AVERROR_EOF;
		}
		catch ( Exception )
		{
			return AVERROR_EOF;
		}
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	static long SeekStream( void* opaque, long offset, int whence )
	{
		try
		{
			var stream = ( (VideoDecoder)GCHandle.FromIntPtr( (nint)opaque ).Target ).input;

			if ( ( whence & AVSEEK_SIZE ) != 0 ) return stream.Length;

			//AVSEEK_FORCE asks for a seek even where it is dear, which a file never is.
			var origin = ( whence & ~0x20000 ) switch
			{
				1 => SeekOrigin.Current,
				2 => SeekOrigin.End,
				_ => SeekOrigin.Begin,
			};

			return stream.Seek( offset, origin );
		}
		catch ( Exception )
		{
			return AVERROR_EINVAL;
		}
	}
}
