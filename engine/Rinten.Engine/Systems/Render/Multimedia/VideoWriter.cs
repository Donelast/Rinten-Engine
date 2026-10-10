using NativeEngine;
namespace Rinten;

/// <summary>Allows the creation of video content by encoding a sequence of frames.</summary>
public sealed class VideoWriter : IDisposable
{
	public struct Config
	{
		//note: keeping file save path out of this because we'll probably only want to expose that carefully, and allow other write options

		public int Width;
		public int Height;
		public int FrameRate;
		public int Bitrate;
		public Codec Codec;
		public Container Container;
		public EncodingPreset Preset;
		public AudioCodec AudioCodec;
		public bool Transparency;

		/// <summary>Can this container support the codec.</summary>
		public bool IsCodecSupported()
		{
			//Validate codec support based on container
#pragma warning disable CS0618 //Intentional: H264/H265 are obsolete but still accepted for backward compat
			return Container switch
			{
				Container.MP4 => Codec is Codec.AV1 or Codec.VP9 or Codec.H264 or Codec.H265,
				Container.WebM => Codec is Codec.AV1 or Codec.VP9 or Codec.VP8,
				Container.WebP => Codec == Codec.WebP,
				_ => false,
			};
#pragma warning restore CS0618
		}

#pragma warning disable CS0618 //Intentional: obsolete codecs still map for backward compat
		internal string CodecName => Codec switch
		{
			Codec.VP8 or Codec.H264 or Codec.H265 => "av1",
			Codec.VP9 => "vp9",
			Codec.AV1 => "av1",
			Codec.WebP => "webp",
			_ => null,
		};
#pragma warning restore CS0618

		internal string AudioCodecName => AudioCodec switch
		{
			AudioCodec.Opus => "opus",
			_ => "opus",
		};

		internal string ContainerName => Container.ToString().ToLower();
	}

	[Expose]
	public enum Codec
	{
		/// <summary>Obsolete: H.264 is no longer supported, if used will map to AV1 instead.</summary>
		[Obsolete( "H.264 is no longer supported, use VP9 instead" )]
		H264,

		/// <summary>Obsolete: H.265 is no longer supported, if used will map to AV1 instead.</summary>
		[Obsolete( "H.265 is no longer supported, use VP9 instead" )]
		H265,

		/// <summary>Obsolete: VP8 is no longer supported, if used will map to VP9 instead.</summary>
		[Obsolete( "VP8 is no longer supported, use VP9 instead" )]
		VP8,

		/// <summary>VP9 codec (supports transparency)</summary>
		VP9,

		/// <summary>WebP codec (supports transparency)</summary>
		WebP,

		/// <summary>AV1 codec — excellent compression, slower encoding.</summary>
		AV1,
	}

	/// <summary>Controls the speed/quality tradeoff of video encoding.</summary>
	[Expose]
	public enum EncodingPreset
	{
		/// <summary>Optimized for speed. Suitable for real-time recording.</summary>
		Fast,

		/// <summary>Balanced speed and quality.</summary>
		Balanced,

		/// <summary>Optimized for quality. Suitable for offline export.</summary>
		Quality,
	}

	/// <summary>Audio codec to use for encoding.</summary>
	[Expose]
	public enum AudioCodec
	{
		/// <summary>Opus — high quality, open codec.</summary>
		Opus,

	}

	[Expose]
	public enum Container
	{
		/// <summary>MP4 container (does not support transparency)</summary>
		MP4,

		/// <summary>WebM container (supports transparency)</summary>
		WebM,

		/// <summary>WebP container (supports transparency)</summary>
		WebP,
	}



	private readonly string path;
	private readonly int width;
	private readonly int height;
	private readonly int frameRate;
	private readonly int bitrate;

	public int Width => width;
	public int Height => height;

	//Whether this writer is writing anything: false where FFmpeg is not there or the file would not open, and every frame is then refused.
	internal bool IsWriting => encoder is not null;

	//Always, here: AddFrame encodes the frame before it returns, under the encoder's lock, so there is no queue to fill and a frame is never dropped for want of room. Kept so code that waits on it works unchanged.
	/// <summary>Do we have space in the encoding queue for another frame?</summary>
	public bool ReadyForVideoFrame => true;

	//FFmpeg's side of the writing, made as the writer is and let go of as it finishes. Every call into it is
	//under its own lock: frames come from whichever thread the caller encodes on, sound from the mixer's.
	private Encoder encoder;

	internal VideoWriter( string path, Config config ) : this( path, config, false )
	{
	}

	//A writer that also takes sound - see AddSound. What records the screen asks for one; a writer
	//made for frames alone writes a file with no sound in it rather than one with a silent track.
	internal VideoWriter( string path, Config config, bool withSound )
	{
		if ( !config.IsCodecSupported() )
			throw new ArgumentException( $"{config.Container} container does not support {config.Codec} codec" );

		this.path = path;

		width = config.Width;
		height = config.Height;
		frameRate = config.FrameRate > 0 ? config.FrameRate : 60;
		bitrate = config.Bitrate > 0 ? config.Bitrate : 8;

		if ( !FFmpeg.IsAvailable )
		{
			FFmpeg.SayUnavailable();
			return;
		}

		if ( width <= 0 || height <= 0 || string.IsNullOrWhiteSpace( path ) )
		{
			Log.Warning( $"video: nothing will be written to {path} - a video has to have a size" );
			return;
		}

		encoder = Encoder.Open( path, config, frameRate, bitrate, withSound );
	}

	~VideoWriter()
	{
		MainThread.QueueDispose( this );
	}

	/// <summary>Dispose this recorder, the encoder will be flushed and video finalized.</summary>
	public void Dispose()
	{
		GC.SuppressFinalize( this );

		var finishing = encoder;
		encoder = null;

		finishing?.Finish();
	}

	/// <summary>Finish creating this video. The encoder will be flushed and video finalized.</summary>
	public Task FinishAsync()
	{
		var finishing = encoder;
		encoder = null;

		if ( finishing is null ) return Task.CompletedTask;

		//Flushing an encoder that holds a second of pictures back is seconds of work, and the caller awaiting this is often the main thread.
		return Task.Run( finishing.Finish );
	}

	/// <summary>Add a frame of data to be encoded. Timestamp is in microseconds. If a timestamp is not specified, it will use an incremented frame count as the timestamp.</summary>
	public unsafe bool AddFrame( ReadOnlySpan<byte> data, TimeSpan? timestamp = default )
	{
		return encoder?.AddFrame( data, width, height, timestamp ) ?? false;
	}

	/// <summary>Add a frame of data to be encoded. Timestamp is in microseconds. If a timestamp is not specified, it will use an incremented frame count as the timestamp.</summary>
	public unsafe bool AddFrame( Bitmap bitmap, TimeSpan? timestamp = default )
	{
		return AddFrame( bitmap.GetBuffer(), timestamp );
	}

	//A frame whose pixels are blue first - what a window's picture often is - converted with everything else rather than turned round beforehand.
	internal bool AddFrameBgra( ReadOnlySpan<byte> data, int dataWidth, int dataHeight, TimeSpan? timestamp )
	{
		return encoder?.AddFrame( data, dataWidth, dataHeight, timestamp, FFmpeg.AV_PIX_FMT_BGRA ) ?? false;
	}

	//Sound to go with the pictures: interleaved floats at a rate, from the start of the
	//recording onwards without gaps. Nothing where this writer was not made for sound.
	internal void AddSound( ReadOnlySpan<float> interleaved, int channels, int rate )
	{
		encoder?.AddSound( interleaved, channels, rate );
	}

	//FFmpeg encoder: royalty-free codecs only (LGPL build), AV1 via SVT-AV1/libaom/rav1e, VP9, Opus
	private sealed unsafe class Encoder
	{
		static readonly Logger log = new( "Video" );

		readonly object gate = new();
		readonly string path;

		void* output;
		void* video;
		void* videoStream;
		void* sound;
		void* soundStream;
		void* picture;
		void* source;
		void* soundFrame;
		void* packet;
		void* scaler;
		void* resampler;

		int encodedWidth;
		int encodedHeight;
		int frameRate;
		(int Num, int Den) videoTimeBase;
		(int Num, int Den) soundTimeBase;

		long lastPts = -1;
		long soundPts;
		int soundFrameSize;
		int soundFormat;
		int soundBytes;
		byte[] soundQueue = Array.Empty<byte>();
		int soundQueued;
		int resampleRate;
		int resampleChannels;
		bool failed;
		bool finished;

		Encoder( string path ) => this.path = path;

		public static Encoder Open( string path, Config config, int frameRate, int bitrate, bool withSound )
		{
			var encoder = new Encoder( path );

			try
			{
				if ( encoder.Start( config, frameRate, bitrate, withSound ) ) return encoder;
			}
			catch ( Exception e )
			{
				log.Warning( $"{path}: {e.Message}" );
			}

			encoder.Free();
			return null;
		}

		//The encoders a codec may be written with, best first.
		static string[] EncodersFor( string codec ) => codec switch
		{
			"vp9" => ["libvpx-vp9"],
			"webp" => ["libwebp_anim", "libwebp"],
			_ => ["libsvtav1", "libaom-av1", "librav1e"],
		};

		bool Start( Config config, int rate, int bitrate, bool withSound )
		{
			frameRate = rate;

			var folder = System.IO.Path.GetDirectoryName( System.IO.Path.GetFullPath( path ) );
			if ( !string.IsNullOrEmpty( folder ) ) System.IO.Directory.CreateDirectory( folder );

			void* made = null;
			int result;

			fixed ( byte* name = FFmpeg.Text( config.ContainerName ) )
			fixed ( byte* file = FFmpeg.Text( path ) )
				result = FFmpeg.avformat_alloc_output_context2( &made, null, name, file );

			if ( result < 0 || made is null ) return Fail( $"no {config.ContainerName} container: {FFmpeg.Error( result )}" );

			output = made;

			var outputFormat = FFmpeg.Pointer( output, FFmpeg.Offsets.FormatOutputFormat );
			var formatFlags = FFmpeg.Int( outputFormat, FFmpeg.Offsets.OutputFormatFlags );
			var globalHeader = ( formatFlags & FFmpeg.AVFMT_GLOBALHEADER ) != 0;

			//Even, because the pictures are four-to-one in colour and a colour sample cannot stand for half a pixel.
			encodedWidth = Math.Max( config.Width & ~1, 2 );
			encodedHeight = Math.Max( config.Height & ~1, 2 );

			string used = null;

			foreach ( var name in EncodersFor( config.CodecName ) )
			{
				video = OpenVideo( name, config, bitrate, globalHeader );

				if ( video is not null )
				{
					used = name;
					break;
				}
			}

			if ( video is null ) return Fail( $"this FFmpeg has no encoder for {config.CodecName}" );

			videoStream = FFmpeg.avformat_new_stream( output, null );
			if ( videoStream is null ) return Fail( "no stream for the pictures" );

			videoTimeBase = (FFmpeg.Int( video, FFmpeg.Offsets.CodecTimeBase ), FFmpeg.Int( video, FFmpeg.Offsets.CodecTimeBase + 4 ));
			FFmpeg.SetRational( videoStream, FFmpeg.Offsets.StreamTimeBase, videoTimeBase.Num, videoTimeBase.Den );

			if ( FFmpeg.avcodec_parameters_from_context( FFmpeg.Pointer( videoStream, FFmpeg.Offsets.StreamCodecParameters ), video ) < 0 )
				return Fail( "the pictures' stream would not take its encoder's settings" );

			//A picture has no sound to go with it, and an animated WebP has nowhere to put one.
			if ( withSound && config.Container != Container.WebP )
			{
				sound = OpenSound( globalHeader );

				if ( sound is null )
				{
					log.Warning( $"{path}: no Opus encoder in this FFmpeg - the video is written without sound" );
				}
				else
				{
					soundStream = FFmpeg.avformat_new_stream( output, null );
					if ( soundStream is null ) return Fail( "no stream for the sound" );

					soundTimeBase = (1, 48000);
					FFmpeg.SetRational( soundStream, FFmpeg.Offsets.StreamTimeBase, 1, 48000 );

					if ( FFmpeg.avcodec_parameters_from_context( FFmpeg.Pointer( soundStream, FFmpeg.Offsets.StreamCodecParameters ), sound ) < 0 )
						return Fail( "the sound's stream would not take its encoder's settings" );
				}
			}

			if ( ( formatFlags & FFmpeg.AVFMT_NOFILE ) == 0 )
			{
				fixed ( byte* file = FFmpeg.Text( path ) )
					result = FFmpeg.avio_open( (void**)( (byte*)output + FFmpeg.Offsets.FormatPb ), file, FFmpeg.AVIO_FLAG_WRITE );

				if ( result < 0 ) return Fail( $"could not write to it: {FFmpeg.Error( result )}" );
			}

			void* options = null;

			//An MP4 with its index at the front, so that a page can play it before it has all arrived. One more pass over the file as it is finished.
			if ( config.Container == Container.MP4 )
			{
				fixed ( byte* key = FFmpeg.Text( "movflags" ) )
				fixed ( byte* value = FFmpeg.Text( "+faststart" ) )
					FFmpeg.av_dict_set( &options, key, value, 0 );
			}

			result = FFmpeg.avformat_write_header( output, &options );
			FFmpeg.av_dict_free( &options );

			if ( result < 0 ) return Fail( $"the container would not start: {FFmpeg.Error( result )}" );

			picture = FFmpeg.av_frame_alloc();
			source = FFmpeg.av_frame_alloc();
			packet = FFmpeg.av_packet_alloc();
			scaler = FFmpeg.sws_alloc_context();

			if ( picture is null || source is null || packet is null || scaler is null ) return Fail( "no memory for encoding" );

			FFmpeg.SetInt( scaler, FFmpeg.Offsets.ScaleFlags, FFmpeg.SWS_BILINEAR );

			FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameFormat, FFmpeg.Int( video, FFmpeg.Offsets.CodecPixelFormat ) );
			FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameWidth, encodedWidth );
			FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameHeight, encodedHeight );

			if ( FFmpeg.av_frame_get_buffer( picture, 0 ) < 0 ) return Fail( "no memory for a picture" );

			log.Info( $"writing {path}: {encodedWidth}x{encodedHeight} at {frameRate} fps with {used}{( sound is not null ? " and Opus" : "" )}" );

			return true;
		}

		void* OpenVideo( string name, Config config, int bitrate, bool globalHeader )
		{
			void* codec;

			fixed ( byte* text = FFmpeg.Text( name ) )
				codec = FFmpeg.avcodec_find_encoder_by_name( text );

			if ( codec is null ) return null;

			var context = FFmpeg.avcodec_alloc_context3( codec );
			if ( context is null ) return null;

			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecWidth, encodedWidth );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecHeight, encodedHeight );
			FFmpeg.SetRational( context, FFmpeg.Offsets.CodecTimeBase, 1, frameRate );
			FFmpeg.SetRational( context, FFmpeg.Offsets.CodecFrameRate, frameRate, 1 );
			FFmpeg.SetLong( context, FFmpeg.Offsets.CodecBitRate, (long)bitrate * 1_000_000 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecGopSize, frameRate * 2 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecThreadCount, 0 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecPixelFormat, PictureFormat( context, codec, config.Transparency && config.Container != Container.MP4 ) );

			//Said, so that a player reads the colours back the way they were written: BT.709, in the video range,
			//which is what everything assumes of a picture this size and not what FFmpeg writes unless told.
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecColorPrimaries, FFmpeg.AVCOL_BT709 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecColorTransfer, FFmpeg.AVCOL_BT709 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecColorSpace, FFmpeg.AVCOL_BT709 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecColorRange, FFmpeg.AVCOL_RANGE_MPEG );

			if ( globalHeader )
				FFmpeg.SetInt( context, FFmpeg.Offsets.CodecFlags, FFmpeg.Int( context, FFmpeg.Offsets.CodecFlags ) | FFmpeg.AV_CODEC_FLAG_GLOBAL_HEADER );

			var fast = config.Preset == EncodingPreset.Fast;
			var quality = config.Preset == EncodingPreset.Quality;

			//Speed against quality, in each encoder's own words. Fast is what a recording of the screen has to be: it keeps up or it drops pictures.
			switch ( name )
			{
				case "libsvtav1":
					FFmpeg.SetOption( context, "preset", fast ? "10" : quality ? "5" : "8" );
					break;

				case "libaom-av1":
					FFmpeg.SetOption( context, "cpu-used", fast ? "8" : quality ? "4" : "6" );
					FFmpeg.SetOption( context, "row-mt", "1" );
					if ( fast ) FFmpeg.SetOption( context, "usage", "realtime" );
					break;

				case "librav1e":
					FFmpeg.SetOption( context, "speed", fast ? "10" : quality ? "6" : "8" );
					break;

				case "libvpx-vp9":
					FFmpeg.SetOption( context, "deadline", fast ? "realtime" : "good" );
					FFmpeg.SetOption( context, "cpu-used", fast ? "8" : quality ? "2" : "4" );
					FFmpeg.SetOption( context, "row-mt", "1" );
					break;

				case "libwebp_anim":
				case "libwebp":
					FFmpeg.SetOption( context, "quality", fast ? "70" : quality ? "90" : "80" );
					break;
			}

			var result = FFmpeg.avcodec_open2( context, codec, null );

			if ( result < 0 )
			{
				log.Warning( $"{name} would not open: {FFmpeg.Error( result )}" );
				FFmpeg.avcodec_free_context( &context );
				return null;
			}

			return context;
		}

		//The picture format to encode in: 4:2:0, with alpha where it was asked for and the encoder has it, and otherwise whatever the encoder lists first.
		static int PictureFormat( void* context, void* codec, bool alpha )
		{
			void* listed = null;
			var count = 0;

			if ( FFmpeg.avcodec_get_supported_config( context, codec, FFmpeg.AV_CODEC_CONFIG_PIX_FORMAT, 0, &listed, &count ) < 0 || listed is null || count <= 0 )
				return FFmpeg.AV_PIX_FMT_YUV420P;

			var formats = new ReadOnlySpan<int>( listed, count );

			if ( alpha && formats.Contains( FFmpeg.AV_PIX_FMT_YUVA420P ) ) return FFmpeg.AV_PIX_FMT_YUVA420P;
			if ( formats.Contains( FFmpeg.AV_PIX_FMT_YUV420P ) ) return FFmpeg.AV_PIX_FMT_YUV420P;

			return formats[0];
		}

		void* OpenSound( bool globalHeader )
		{
			void* codec;

			fixed ( byte* text = FFmpeg.Text( "libopus" ) )
				codec = FFmpeg.avcodec_find_encoder_by_name( text );

			if ( codec is null ) return null;

			var context = FFmpeg.avcodec_alloc_context3( codec );
			if ( context is null ) return null;

			//Interleaved floats where it takes them, which is what the mixer has; sixteen bits otherwise. libopus takes both.
			void* listed = null;
			var count = 0;
			var format = FFmpeg.AV_SAMPLE_FMT_FLT;

			if ( FFmpeg.avcodec_get_supported_config( context, codec, FFmpeg.AV_CODEC_CONFIG_SAMPLE_FORMAT, 0, &listed, &count ) >= 0 && listed is not null && count > 0 )
			{
				var formats = new ReadOnlySpan<int>( listed, count );
				format = formats.Contains( FFmpeg.AV_SAMPLE_FMT_FLT ) ? FFmpeg.AV_SAMPLE_FMT_FLT : FFmpeg.AV_SAMPLE_FMT_S16;

				if ( !formats.Contains( format ) )
				{
					FFmpeg.avcodec_free_context( &context );
					return null;
				}
			}

			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecSampleRate, 48000 );
			FFmpeg.SetInt( context, FFmpeg.Offsets.CodecSampleFormat, format );
			FFmpeg.SetRational( context, FFmpeg.Offsets.CodecTimeBase, 1, 48000 );
			FFmpeg.SetLong( context, FFmpeg.Offsets.CodecBitRate, 160_000 );
			FFmpeg.av_channel_layout_default( (byte*)context + FFmpeg.Offsets.CodecChannelLayout, 2 );

			if ( globalHeader )
				FFmpeg.SetInt( context, FFmpeg.Offsets.CodecFlags, FFmpeg.Int( context, FFmpeg.Offsets.CodecFlags ) | FFmpeg.AV_CODEC_FLAG_GLOBAL_HEADER );

			var result = FFmpeg.avcodec_open2( context, codec, null );

			if ( result < 0 )
			{
				log.Warning( $"libopus would not open: {FFmpeg.Error( result )}" );
				FFmpeg.avcodec_free_context( &context );
				return null;
			}

			soundFormat = format;
			soundBytes = format == FFmpeg.AV_SAMPLE_FMT_FLT ? 4 : 2;
			soundFrameSize = Math.Max( FFmpeg.Int( context, FFmpeg.Offsets.CodecFrameSize ), 960 );

			soundFrame = FFmpeg.av_frame_alloc();
			if ( soundFrame is null )
			{
				FFmpeg.avcodec_free_context( &context );
				return null;
			}

			return context;
		}

		bool Fail( string why )
		{
			log.Warning( $"video: {path} will not be written - {why}" );
			failed = true;
			return false;
		}

		public bool AddFrame( ReadOnlySpan<byte> data, int dataWidth, int dataHeight, TimeSpan? timestamp, int pixelFormat = FFmpeg.AV_PIX_FMT_RGBA )
		{
			if ( dataWidth <= 0 || dataHeight <= 0 || data.Length < dataWidth * dataHeight * 4 ) return false;

			lock ( gate )
			{
				if ( failed || finished || video is null ) return false;

				var pts = timestamp is { } at ? (long)Math.Round( at.TotalSeconds * frameRate ) : lastPts + 1;

				//Two pictures for the same moment: the second is one this rate has no room for, and dropping it is what a lower rate means.
				if ( pts <= lastPts ) return timestamp is not null;

				if ( FFmpeg.av_frame_make_writable( picture ) < 0 ) return false;

				fixed ( byte* pixels = data )
				{
					FFmpeg.SetPointer( source, FFmpeg.Offsets.FrameData, pixels );
					FFmpeg.SetInt( source, FFmpeg.Offsets.FrameLineSize, dataWidth * 4 );
					FFmpeg.SetInt( source, FFmpeg.Offsets.FrameWidth, dataWidth );
					FFmpeg.SetInt( source, FFmpeg.Offsets.FrameHeight, dataHeight );
					FFmpeg.SetInt( source, FFmpeg.Offsets.FrameFormat, pixelFormat );

					FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameColorPrimaries, FFmpeg.AVCOL_BT709 );
					FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameColorTransfer, FFmpeg.AVCOL_BT709 );
					FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameColorSpace, FFmpeg.AVCOL_BT709 );
					FFmpeg.SetInt( picture, FFmpeg.Offsets.FrameColorRange, FFmpeg.AVCOL_RANGE_MPEG );

					var scaled = FFmpeg.sws_scale_frame( scaler, picture, source );

					FFmpeg.SetPointer( source, FFmpeg.Offsets.FrameData, null );

					if ( scaled < 0 ) return Fail( $"a picture would not convert: {FFmpeg.Error( scaled )}" );
				}

				FFmpeg.SetLong( picture, FFmpeg.Offsets.FramePts, pts );
				lastPts = pts;

				return Encode( video, picture, videoStream, videoTimeBase );
			}
		}

		public void AddSound( ReadOnlySpan<float> interleaved, int channels, int rate )
		{
			if ( interleaved.IsEmpty || channels <= 0 || rate <= 0 ) return;

			lock ( gate )
			{
				if ( failed || finished || sound is null ) return;

				if ( resampler is null || resampleRate != rate || resampleChannels != channels )
				{
					if ( resampler is not null ) { var old = resampler; FFmpeg.swr_free( &old ); resampler = null; }

					var into = stackalloc byte[FFmpeg.Offsets.ChannelLayoutSize];
					var from = stackalloc byte[FFmpeg.Offsets.ChannelLayoutSize];
					new Span<byte>( into, FFmpeg.Offsets.ChannelLayoutSize ).Clear();
					new Span<byte>( from, FFmpeg.Offsets.ChannelLayoutSize ).Clear();

					FFmpeg.av_channel_layout_default( into, 2 );
					FFmpeg.av_channel_layout_default( from, channels );

					void* made = null;
					var result = FFmpeg.swr_alloc_set_opts2( &made, into, soundFormat, 48000, from, FFmpeg.AV_SAMPLE_FMT_FLT, rate, 0, null );

					FFmpeg.av_channel_layout_uninit( into );
					FFmpeg.av_channel_layout_uninit( from );

					if ( result < 0 || made is null || FFmpeg.swr_init( made ) < 0 )
					{
						if ( made is not null ) FFmpeg.swr_free( &made );
						return;
					}

					resampler = made;
					resampleRate = rate;
					resampleChannels = channels;
				}

				var frames = interleaved.Length / channels;
				var room = FFmpeg.swr_get_out_samples( resampler, frames );
				if ( room <= 0 ) return;

				var frameBytes = 2 * soundBytes;
				var need = ( soundQueued + ( room * frameBytes ) );

				if ( soundQueue.Length < need ) Array.Resize( ref soundQueue, Math.Max( need, soundQueue.Length * 2 ) );

				int converted;

				fixed ( float* input = interleaved )
				fixed ( byte* queue = soundQueue )
				{
					var output = queue + soundQueued;
					var planes = (byte*)input;
					converted = FFmpeg.swr_convert( resampler, &output, room, &planes, frames );
				}

				if ( converted <= 0 ) return;

				soundQueued += converted * frameBytes;

				EncodeQueuedSound( padLast: false );
			}
		}

		//Whole Opus frames out of what is queued - and, at the end, the last part frame padded with silence.
		void EncodeQueuedSound( bool padLast )
		{
			var frameBytes = 2 * soundBytes;
			var whole = soundFrameSize * frameBytes;

			while ( soundQueued >= whole || ( padLast && soundQueued > 0 ) )
			{
				var take = Math.Min( soundQueued, whole );

				FFmpeg.av_frame_unref( soundFrame );
				FFmpeg.SetInt( soundFrame, FFmpeg.Offsets.FrameSampleCount, soundFrameSize );
				FFmpeg.SetInt( soundFrame, FFmpeg.Offsets.FrameFormat, soundFormat );
				FFmpeg.SetInt( soundFrame, FFmpeg.Offsets.FrameSampleRate, 48000 );
				FFmpeg.av_channel_layout_default( (byte*)soundFrame + FFmpeg.Offsets.FrameChannelLayout, 2 );

				if ( FFmpeg.av_frame_get_buffer( soundFrame, 0 ) < 0 ) return;

				var into = (byte*)FFmpeg.Pointer( soundFrame, FFmpeg.Offsets.FrameData );
				var span = new Span<byte>( into, whole );

				soundQueue.AsSpan( 0, take ).CopyTo( span );
				if ( take < whole ) span[take..].Clear();

				soundQueue.AsSpan( take, soundQueued - take ).CopyTo( soundQueue );
				soundQueued -= take;

				FFmpeg.SetLong( soundFrame, FFmpeg.Offsets.FramePts, soundPts );
				soundPts += soundFrameSize;

				if ( !Encode( sound, soundFrame, soundStream, soundTimeBase ) ) return;
			}
		}

		//One frame into an encoder - or null, which empties it - and whatever it hands back into the container, stamped in the stream's own time base.
		bool Encode( void* codec, void* frame, void* stream, (int Num, int Den) codecBase )
		{
			var sent = FFmpeg.avcodec_send_frame( codec, frame );

			if ( sent < 0 && sent != FFmpeg.AVERROR_EOF ) return Fail( $"the encoder refused a frame: {FFmpeg.Error( sent )}" );

			var index = FFmpeg.Int( stream, FFmpeg.Offsets.StreamIndex );
			var streamNum = FFmpeg.Int( stream, FFmpeg.Offsets.StreamTimeBase );
			var streamDen = FFmpeg.Int( stream, FFmpeg.Offsets.StreamTimeBase + 4 );

			while ( true )
			{
				var received = FFmpeg.avcodec_receive_packet( codec, packet );

				if ( received == FFmpeg.AVERROR_EAGAIN || received == FFmpeg.AVERROR_EOF ) return true;
				if ( received < 0 ) return Fail( $"the encoder failed: {FFmpeg.Error( received )}" );

				Restamp( FFmpeg.Offsets.PacketPts, codecBase, streamNum, streamDen );
				Restamp( FFmpeg.Offsets.PacketDts, codecBase, streamNum, streamDen );

				var duration = FFmpeg.Long( packet, FFmpeg.Offsets.PacketDuration );
				if ( duration > 0 )
					FFmpeg.SetLong( packet, FFmpeg.Offsets.PacketDuration, FFmpeg.Rescale( duration, (long)codecBase.Num * streamDen, (long)codecBase.Den * streamNum ) );

				FFmpeg.SetInt( packet, FFmpeg.Offsets.PacketStreamIndex, index );

				var written = FFmpeg.av_interleaved_write_frame( output, packet );
				if ( written < 0 ) return Fail( $"the container would not take a packet: {FFmpeg.Error( written )}" );
			}
		}

		void Restamp( int offset, (int Num, int Den) from, int toNum, int toDen )
		{
			var stamp = FFmpeg.Long( packet, offset );
			if ( stamp == FFmpeg.AV_NOPTS_VALUE ) return;

			FFmpeg.SetLong( packet, offset, FFmpeg.Rescale( stamp, (long)from.Num * toDen, (long)from.Den * toNum ) );
		}

		/// <summary>Empties both encoders, closes the container and lets go of everything.</summary>
		public void Finish()
		{
			lock ( gate )
			{
				if ( finished ) return;

				try
				{
					if ( !failed && output is not null )
					{
						if ( sound is not null )
						{
							EncodeQueuedSound( padLast: true );
							Encode( sound, null, soundStream, soundTimeBase );
						}

						if ( video is not null ) Encode( video, null, videoStream, videoTimeBase );

						var trailer = FFmpeg.av_write_trailer( output );
						if ( trailer < 0 ) log.Warning( $"video: {path} did not finish cleanly: {FFmpeg.Error( trailer )}" );
					}
				}
				finally
				{
					finished = true;
					Free();
				}
			}
		}

		void Free()
		{
			if ( scaler is not null ) { var s = scaler; FFmpeg.sws_free_context( &s ); scaler = null; }
			if ( resampler is not null ) { var r = resampler; FFmpeg.swr_free( &r ); resampler = null; }
			if ( picture is not null ) { var f = picture; FFmpeg.av_frame_free( &f ); picture = null; }
			if ( source is not null ) { var f = source; FFmpeg.av_frame_free( &f ); source = null; }
			if ( soundFrame is not null ) { var f = soundFrame; FFmpeg.av_frame_free( &f ); soundFrame = null; }
			if ( packet is not null ) { var p = packet; FFmpeg.av_packet_free( &p ); packet = null; }
			if ( video is not null ) { var c = video; FFmpeg.avcodec_free_context( &c ); video = null; }
			if ( sound is not null ) { var c = sound; FFmpeg.avcodec_free_context( &c ); sound = null; }

			if ( output is not null )
			{
				var outputFormat = FFmpeg.Pointer( output, FFmpeg.Offsets.FormatOutputFormat );

				if ( ( FFmpeg.Int( outputFormat, FFmpeg.Offsets.OutputFormatFlags ) & FFmpeg.AVFMT_NOFILE ) == 0 )
					FFmpeg.avio_closep( (void**)( (byte*)output + FFmpeg.Offsets.FormatPb ) );

				FFmpeg.avformat_free_context( output );
				output = null;
			}
		}
	}
}
