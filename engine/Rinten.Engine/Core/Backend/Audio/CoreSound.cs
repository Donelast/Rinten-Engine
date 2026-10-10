using System.IO;
using Rinten.Native;

namespace Rinten.Audio;

//A sound kept as file bytes and decoded while playing; ogg is decoded whole at load instead
internal sealed unsafe class CoreSound
{
	//The file as it was written, on the pinned heap: a decoder reads from these in place for as long as it lives, and they must not move.
	internal byte[] Bytes { get; private set; }

	//Or, for a format that can only be decoded in one go, the samples themselves - interleaved, at SampleRate.
	internal float[] Pcm { get; private set; }

	public int ChannelCount { get; private set; }

	public int SampleRate { get; private set; }

	/// <summary>In the file's own frames. Zero when the format cannot say without being read through.</summary>
	public long Frames { get; private set; }

	public float Duration => SampleRate <= 0 ? 0.0f : Frames / (float)SampleRate;

	/// <summary>The loop the file itself marks, in its own frames: where it starts, or -1 when it marks none. Read out of a WAV's "smpl" or "cue " chunk - see <see cref="SoundData.ReadLoop"/>.</summary>
	public long DeclaredLoopStart { get; private set; } = -1;

	/// <summary>One past the last frame of <see cref="DeclaredLoopStart"/>'s loop, or 0 for the end of the sound.</summary>
	public long DeclaredLoopEnd { get; private set; }

	/// <summary>Where a looping playing goes back to when it reaches <see cref="LoopEnd"/>, in the file's frames. Whether it loops at all is the playing's business; this is only where.</summary>
	public long LoopStart { get; private set; }

	/// <summary>One past the last frame of the loop, in the file's frames, or 0 for the end of the sound. A playing that is not looping plays on past it to the end - which is how a sound with a tail after its loop finishes once it is let go of.</summary>
	public long LoopEnd { get; private set; }

	//Sets the region a loop plays, in the file's frames, made sensible: a start past the end is the start, and an
	//end at or before the start is the end of the sound. Set once, by whoever loaded it, before anything plays it.
	internal void SetLoop( long start, long end )
	{
		start = Math.Max( start, 0 );
		end = Math.Max( end, 0 );

		if ( Frames > 0 )
		{
			if ( start >= Frames ) start = 0;
			if ( end > Frames ) end = 0;
		}

		if ( end != 0 && end <= start ) end = 0;

		LoopStart = start;
		LoopEnd = end;
	}

	/// <summary>What the file says about itself in its tags - title, artist, album - read once when it was loaded. Empty for a file that says nothing.</summary>
	public IReadOnlyDictionary<string, string> Tags { get; private set; } = new Dictionary<string, string>();

	private CoreSound() { }

	/// <summary>Reads a file, or null if it is not there or not a sound.</summary>
	public static CoreSound Load( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return null;

		byte[] bytes;

		try
		{
			if ( !File.Exists( path ) ) return null;

			bytes = SealedFile.ReadAllBytes( path );
		}
		catch ( IOException )
		{
			return null;
		}
		catch ( UnauthorizedAccessException )
		{
			return null;
		}

		return FromMemory( bytes );
	}

	/// <summary>Holds a sound handed over as bytes, or null if they are not one.</summary>
	public static CoreSound FromMemory( ReadOnlySpan<byte> data )
	{
		if ( data.Length == 0 ) return null;

		var sound = new CoreSound();

		//Copied onto the pinned heap, because a decoder keeps a pointer to them and the runtime would otherwise be free to move them.
		sound.Bytes = GC.AllocateUninitializedArray<byte>( data.Length, pinned: true );
		data.CopyTo( sound.Bytes );

		//Before Describe: an ogg's bytes are let go of once it is decoded.
		sound.Tags = SoundTags.Read( data );

		( sound.DeclaredLoopStart, sound.DeclaredLoopEnd ) = SoundData.ReadLoop( data );

		return sound.Describe() ? sound : null;
	}

	//Whether a file starts the way an ogg does.
	private static bool IsOgg( ReadOnlySpan<byte> bytes )
		=> bytes.Length > 4 && bytes[0] == (byte)'O' && bytes[1] == (byte)'g' && bytes[2] == (byte)'g' && bytes[3] == (byte)'S';

	//Decode an ogg in one go, because it cannot be decoded any other way here.
	private bool DecodeOgg()
	{
		int channels, rate;
		short* samples = null;
		int frames;

		fixed ( byte* data = Bytes )
		{
			frames = MiniAudio.rn_vorbis_decode( data, Bytes.Length, &channels, &rate, &samples );
		}

		if ( frames <= 0 || samples is null || channels <= 0 || rate <= 0 )
		{
			if ( samples is not null ) MiniAudio.rn_vorbis_free( samples );
			return false;
		}

		ChannelCount = channels;
		SampleRate = rate;
		Frames = frames;

		Pcm = new float[(long)frames * channels];

		for ( var i = 0; i < Pcm.Length; i++ ) Pcm[i] = samples[i] / 32768.0f;

		MiniAudio.rn_vorbis_free( samples );

		//The bytes are no longer worth keeping: what plays is the samples.
		Bytes = null;

		return true;
	}

	//Read what the file says about itself, without keeping the decoder.
	private bool Describe()
	{
		if ( IsOgg( Bytes ) ) return DecodeOgg();

		int channels, rate;
		ulong frames;
		void* decoder;

		fixed ( byte* data = Bytes )
		{
			decoder = MiniAudio.rn_decoder_open( data, (nuint)Bytes.Length, 0, 0, &channels, &rate, &frames );
		}

		if ( decoder is null ) return false;

		ChannelCount = channels;
		SampleRate = rate;

		//Not every format knows its own length without being read through - an mp3 from a stream does not. Zero means "until it stops".
		Frames = (long)frames;

		MiniAudio.rn_decoder_close( decoder );

		return ChannelCount > 0 && SampleRate > 0;
	}
}
