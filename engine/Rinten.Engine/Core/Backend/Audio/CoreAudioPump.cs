using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using Rinten.Native;

namespace Rinten.Audio;

//Audio output pump over miniaudio: feeds the device from the render callback, never blocking
internal sealed unsafe class CoreAudioPump : IDisposable
{
	private void* device;
	private GCHandle self;

	private readonly delegate* unmanaged<void*, float*, int, int, void> render;
	private readonly void* user;

	//Silence without stopping. A device that is closed while the game is muted has to be opened again to unmute,
	//and opening one takes long enough to be heard. The pump keeps running and the samples are thrown away.
	private int muted;

	//How many frames have gone out since it opened. What a test watches to know the pump is running,
	//and the only number here that is read from another thread while the audio thread writes it.
	private long frames;

	private CoreAudioPump( delegate* unmanaged<void*, float*, int, int, void> render, void* user )
	{
		this.render = render;
		this.user = user;
	}

	/// <summary>Opens and starts a device, or null when there is none to be had.</summary>
	public static CoreAudioPump Open( int kind, int sampleRate, int channels, int framesPerBuffer,
		delegate* unmanaged<void*, float*, int, int, void> render, void* user )
	{
		if ( sampleRate <= 0 ) sampleRate = 44100;
		if ( channels <= 0 ) channels = 2;
		if ( framesPerBuffer <= 0 ) framesPerBuffer = 512;

		var pump = new CoreAudioPump( render, user );

		//The callback finds its way back to this object through the pointer the device carries: a handle
		//rather than a pinned object, because the runtime does not pin objects with references in them.
		pump.self = GCHandle.Alloc( pump );

		int gotRate, gotChannels;
		var name = stackalloc byte[256];

		pump.device = MiniAudio.rn_audio_device_open( kind, 0, sampleRate, channels, framesPerBuffer,
			&Pump, (void*)GCHandle.ToIntPtr( pump.self ), &gotRate, &gotChannels, name, 256 );

		if ( pump.device is null )
		{
			pump.self.Free();
			return null;
		}

		//What we asked for and what we got are not always the same: the device decides, and everything above has to be told which it is.
		pump.SampleRate = gotRate;
		pump.ChannelCount = gotChannels;
		pump.Name = Marshal.PtrToStringUTF8( (IntPtr)name ) ?? "";

		if ( MiniAudio.rn_audio_device_start( pump.device ) == 0 )
		{
			pump.Dispose();
			return null;
		}

		return pump;
	}

	public bool IsValid => device is not null;

	public string Name { get; private set; } = "";

	public int ChannelCount { get; private set; }

	public int SampleRate { get; private set; }

	public bool Muted
	{
		get => Volatile.Read( ref muted ) != 0;
		set => Volatile.Write( ref muted, value ? 1 : 0 );
	}

	/// <summary>How many frames the device has asked for since it opened.</summary>
	public ulong FramesRendered => (ulong)Volatile.Read( ref frames );

	//What the operating system calls when it wants more. Everything on this path is either a memset or a call into
	//the render callback, which was written to be called from exactly here. Nothing allocates and nothing waits.
	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	[SkipLocalsInit]
	private static void Pump( void* user, float* output, float* input, uint count )
	{
		if ( user is null || output is null ) return;

		var self = GCHandle.FromIntPtr( (IntPtr)user ).Target as CoreAudioPump;
		if ( self is null ) return;

		var frames = (int)count;
		var channels = self.ChannelCount;

		if ( self.render is null || Volatile.Read( ref self.muted ) != 0 )
		{
			Unsafe.InitBlockUnaligned( output, 0, (uint)( frames * channels * sizeof( float ) ) );
		}
		else
		{
			self.render( self.user, output, frames, channels );
		}

		Interlocked.Add( ref self.frames, frames );
	}

	/// <summary>Stops the device and waits for the audio thread to be done with it, before anything the callback calls into can go away.</summary>
	public void Dispose()
	{
		if ( device is null ) return;

		MiniAudio.rn_audio_device_close( device );
		device = null;

		if ( self.IsAllocated ) self.Free();
	}
}
