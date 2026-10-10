using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Rinten.Native;

namespace Rinten.Audio;

//Microphone capture into a buffer allocated once; OpenStream makes it a lock-free ring instead
internal sealed unsafe class CoreAudioCapture : IDisposable
{
	private void* device;
	private GCHandle self;

	//Pinned for its whole life: the audio thread writes into it by pointer.
	private float[] samples;

	//How many frames are in the buffer. Written by the audio thread and read by whoever asks, which is the only value here that crosses.
	private int frames;

	//Whether it filled up and stopped taking anything.
	private int full;

	//Frames the buffer can hold, fixed at open.
	private int capacity;

	//Whether this is a ring read with Take rather than a recording.
	private bool streaming;

	//Where the reader of a ring is, in frames. Only the reader moves it.
	private int readAt;

	private CoreAudioCapture() { }

	/// <summary>Opens the capture device and starts recording, or null when there is no device to record from.</summary>
	public static CoreAudioCapture Open( int sampleRate, int channels, int seconds )
		=> Open( sampleRate, channels, seconds, false );

	/// <summary>Opens the capture device as a ring that is read as it fills, or null when there is no device to record from.</summary>
	public static CoreAudioCapture OpenStream( int sampleRate, int channels, int seconds )
		=> Open( sampleRate, channels, seconds, true );

	private static CoreAudioCapture Open( int sampleRate, int channels, int seconds, bool streaming )
	{
		if ( sampleRate <= 0 ) sampleRate = 44100;
		if ( channels <= 0 ) channels = 1;
		if ( seconds <= 0 ) seconds = 60;

		var capture = new CoreAudioCapture { streaming = streaming };
		capture.self = GCHandle.Alloc( capture );

		int gotRate, gotChannels;

		capture.device = MiniAudio.rn_audio_device_open( MiniAudio.DeviceDefault, 1, sampleRate, channels, 0,
			&Fill, (void*)GCHandle.ToIntPtr( capture.self ), &gotRate, &gotChannels, null, 0 );

		if ( capture.device is null )
		{
			capture.self.Free();
			return null;
		}

		//What the device decided, which is not always what was asked for - and the buffer is sized against
		//the answer, or a device that gave us 48k would record five sixths of the seconds that were asked for.
		capture.SampleRate = gotRate;
		capture.ChannelCount = gotChannels;
		capture.capacity = gotRate * seconds;

		//The one allocation, here, before the device is started and before the audio thread exists.
		capture.samples = GC.AllocateArray<float>( capture.capacity * gotChannels, pinned: true );

		if ( MiniAudio.rn_audio_device_start( capture.device ) == 0 )
		{
			capture.Dispose();
			return null;
		}

		return capture;
	}

	public bool IsValid => device is not null;

	public int SampleRate { get; private set; }

	public int ChannelCount { get; private set; }

	/// <summary>How many frames have been recorded so far - or, for a ring, where the audio thread will write next.</summary>
	public int Frames => Volatile.Read( ref frames );

	/// <summary>Whether the buffer filled and the recording stopped early.</summary>
	public bool Full => Volatile.Read( ref full ) != 0;

	/// <summary>Copies what has been recorded, up to <paramref name="count"/> frames, interleaved. The samples are still there until <see cref="Clear"/>. A ring is read with <see cref="Take"/> instead.</summary>
	public int Read( Span<float> into, int count )
	{
		if ( !IsValid || streaming || count <= 0 ) return 0;

		//Read once and work against that: the audio thread is still writing, and the frames it adds while this copies are simply the next read's.
		var have = Volatile.Read( ref frames );
		var take = Math.Min( count, have );

		if ( take <= 0 ) return 0;

		var length = take * ChannelCount;
		if ( into.Length < length ) return 0;

		samples.AsSpan( 0, length ).CopyTo( into );

		return take;
	}

	/// <summary>Takes what a ring has gathered since the last take, up to what fits in <paramref name="into"/>, interleaved; the frames taken are gone from it. Returns how many frames that was.</summary>
	public int Take( Span<float> into )
	{
		if ( !IsValid || !streaming ) return 0;

		var channels = ChannelCount;
		var write = Volatile.Read( ref frames );
		var read = readAt;

		var waiting = write - read;
		if ( waiting < 0 ) waiting += capacity;

		var take = Math.Min( waiting, into.Length / channels );
		if ( take <= 0 ) return 0;

		//In at most two pieces: up to the end of the ring, and on from its start.
		var first = Math.Min( take, capacity - read );

		samples.AsSpan( read * channels, first * channels ).CopyTo( into );
		samples.AsSpan( 0, ( take - first ) * channels ).CopyTo( into[( first * channels )..] );

		Volatile.Write( ref readAt, ( read + take ) % capacity );

		return take;
	}

	public void Clear()
	{
		if ( streaming )
		{
			//Everything waiting is skipped: the reader catches up to the writer.
			Volatile.Write( ref readAt, Volatile.Read( ref frames ) );
			Volatile.Write( ref full, 0 );
			return;
		}

		Volatile.Write( ref full, 0 );
		Volatile.Write( ref frames, 0 );
	}

	//What the operating system calls when it has more.
	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	[SkipLocalsInit]
	private static void Fill( void* user, float* output, float* input, uint count )
	{
		if ( user is null || input is null ) return;

		var self = GCHandle.FromIntPtr( (IntPtr)user ).Target as CoreAudioCapture;
		if ( self is null ) return;

		if ( self.streaming )
		{
			self.FillRing( input, (int)count );
			return;
		}

		var at = Volatile.Read( ref self.frames );
		var room = self.capacity - at;

		if ( room <= 0 )
		{
			Volatile.Write( ref self.full, 1 );
			return;
		}

		var take = (int)count;
		if ( take > room )
		{
			take = room;
			Volatile.Write( ref self.full, 1 );
		}

		var channels = self.ChannelCount;

		//The array is on the pinned heap, so its address is its address.
		var destination = (float*)Unsafe.AsPointer( ref MemoryMarshal.GetArrayDataReference( self.samples ) );

		Unsafe.CopyBlockUnaligned( destination + ( (long)at * channels ), input, (uint)( take * channels * sizeof( float ) ) );

		Volatile.Write( ref self.frames, at + take );
	}

	//A ring's half of Fill: onto the end, around the end, never past the reader.
	private void FillRing( float* input, int count )
	{
		var write = Volatile.Read( ref frames );
		var read = Volatile.Read( ref readAt );

		var waiting = write - read;
		if ( waiting < 0 ) waiting += capacity;

		//One frame is kept empty, so a full ring and an empty one are told apart.
		var room = capacity - 1 - waiting;
		var take = Math.Min( count, room );

		if ( take < count ) Volatile.Write( ref full, 1 );
		if ( take <= 0 ) return;

		var channels = ChannelCount;
		var destination = (float*)Unsafe.AsPointer( ref MemoryMarshal.GetArrayDataReference( samples ) );

		var first = Math.Min( take, capacity - write );

		Unsafe.CopyBlockUnaligned( destination + ( (long)write * channels ), input, (uint)( first * channels * sizeof( float ) ) );

		if ( take > first )
			Unsafe.CopyBlockUnaligned( destination, input + ( (long)first * channels ), (uint)( ( take - first ) * channels * sizeof( float ) ) );

		Volatile.Write( ref frames, ( write + take ) % capacity );
	}

	/// <summary>Stops before the buffer it writes into goes away, for the same reason playback does.</summary>
	public void Dispose()
	{
		if ( device is null ) return;

		MiniAudio.rn_audio_device_close( device );
		device = null;

		if ( self.IsAllocated ) self.Free();
	}
}
