using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinten.Native;

namespace Rinten.Audio;

//Our audio device: mixes 512-frame rounds on the audio thread and stages them to the device
internal sealed unsafe class CoreAudioDevice : IAudioDeviceBackend
{
	private static readonly Logger log = new( "Audio" );

	//The system's own device.
	internal const int DeviceDefault = MiniAudio.DeviceDefault;

	//A device that goes nowhere, keeping its own clock. What a machine with no sound card gets, what a server runs, and what a test watches.
	internal const int DeviceNull = MiniAudio.DeviceNull;

	//The one device, for the callback to find its way back to. A static rather than a handle passed through the pump, because there is one output
	//device in a process and a managed object cannot be handed to native code as a pointer without pinning it for the life of the program.
	private static CoreAudioDevice current;

	private CoreAudioPump pump;

	//What the mixer has made and the device has not yet taken.
	private float[] staged = Array.Empty<float>();

	private int stagedFrames;
	private int stagedTaken;
	private int stagedChannels;

	//Which device this opened - see Suspend, which opens it again.
	private readonly int kind;

	//Whether the device has been let go while the application is away - see Suspend.
	private bool suspended;

	//Whether it was muted when it was let go, for when it is opened again.
	private bool mutedWhenSuspended;

	public CoreAudioDevice( int kind = DeviceDefault )
	{
		this.kind = kind;

		if ( !CoreNative.Available ) return;

		current = this;

		pump = CoreAudioPump.Open( kind, (int)AudioEngine.SamplingRate, 2, AudioEngine.MixBufferSize, &Render, null );

		if ( pump is null )
		{
			log.Warning( "no audio device - the game will run without sound" );
			current = null;

			return;
		}

		ChannelCount = pump.ChannelCount;
		SampleRate = pump.SampleRate;
		Name = pump.Name;

		//From here on the mixer runs when this device asks and at no other time -
		//the legacy device may still be turning, and nothing good comes of both of them pulling.
		MixingThread.DrivenByOurDevice = true;

		log.Info( $"audio: {Name} ({ChannelCount}ch, {SampleRate}Hz)" );

		//Closed before the runtime goes, whatever else happens. A device that is still asking while managed code is being taken down
		//calls into a callback that no longer exists, which is a crash on the way out of every process that ever played a sound.
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
	}

	private void OnProcessExit( object sender, EventArgs e ) => Shutdown();

	public bool IsValid => pump is not null || suspended;

	public string Name { get; private set; } = "";

	public int ChannelCount { get; private set; } = 2;

	public int SampleRate { get; private set; } = 44100;

	public bool Muted
	{
		get => suspended ? mutedWhenSuspended : pump is not null && pump.Muted;
		set
		{
			if ( suspended ) mutedWhenSuspended = value;
			else if ( pump is not null ) pump.Muted = value;
		}
	}

	//Closes the device while in the background and reopens it on return, so the hardware can sleep
	internal void Suspend( bool suspend )
	{
		if ( suspend == suspended ) return;

		if ( suspend )
		{
			if ( pump is null ) return;

			mutedWhenSuspended = pump.Muted;

			pump.Dispose();
			pump = null;

			suspended = true;
			Clear();
			return;
		}

		suspended = false;

		pump = CoreAudioPump.Open( kind, (int)AudioEngine.SamplingRate, 2, AudioEngine.MixBufferSize, &Render, null );

		if ( pump is null )
		{
			log.Warning( "the audio device did not open again - the game goes on without sound" );
			return;
		}

		//What came back may not be what went: headphones plugged in while away.
		ChannelCount = pump.ChannelCount;
		SampleRate = pump.SampleRate;
		Name = pump.Name;

		pump.Muted = mutedWhenSuspended;
	}

	/// <summary>How many frames the device has asked for since it opened.</summary>
	public ulong FramesRendered => IsValid ? pump.FramesRendered : 0;

	public void Clear()
	{
		//Whatever was mixed and not yet played is dropped. There is nothing further down to clear: the
		//device is holding at most one period, and silencing that would be a click rather than a silence.
		stagedFrames = 0;
		stagedTaken = 0;
	}

	//Interleaved on the way in, because that is what a device wants and the mixer works a channel at a time. Called on the audio thread, from inside the render below.
	/// <summary>One buffer of mixed audio, from the end of a mix.</summary>
	public void Submit( MultiChannelBuffer buffer )
	{
		if ( buffer is null ) return;

		var channels = Math.Min( buffer.ChannelCount, ChannelCount );
		if ( channels <= 0 ) return;

		var frames = AudioEngine.MixBufferSize;
		var wanted = frames * ChannelCount;

		if ( staged.Length < wanted ) staged = new float[wanted];

		for ( var channel = 0; channel < ChannelCount; channel++ )
		{
			//More speakers than the mixer made: the last channel it made is what the rest get, which is quieter than silence but is at least the same sound.
			var samples = buffer.Get( Math.Min( channel, channels - 1 ) ).Buffer;

			for ( var frame = 0; frame < frames; frame++ )
			{
				staged[( frame * ChannelCount ) + channel] = samples[frame];
			}
		}

		stagedFrames = frames;
		stagedTaken = 0;
		stagedChannels = ChannelCount;

		buffersMixed++;
	}

	private ulong buffersMixed;

	//How many buffers the mixer has handed over. The far end of the chain: the device asked, the mixer ran, and what it made arrived
	//here. Counted because there is nothing to listen to in a test, and this is the thing a test would otherwise be listening for.
	internal ulong BuffersMixed => buffersMixed;

	//Fill one of the device's buffers, mixing as much as it takes.
	private void Fill( float* output, int frames, int channels )
	{
		var written = 0;

		while ( written < frames )
		{
			if ( stagedTaken >= stagedFrames )
			{
				stagedFrames = 0;
				stagedTaken = 0;

				//Nothing to mix yet. The device starts the moment it is opened and the engine builds its mixers a little later, and the
				//mixer says so by throwing - which on this thread would be an error in the log every eleven milliseconds until it caught up.
				if ( Mixer.Master is null ) break;

				//One round of the mixer, which ends by handing its output back through Submit above. Asked for directly
				//rather than through the line the legacy device calls, which is switched off while we are the ones playing.
				MixingThread.MixOneBufferNow();

				//The mixer produced nothing - no graph, or it threw. Silence for the rest of this buffer rather than spinning here.
				if ( stagedFrames <= 0 ) break;
			}

			var take = Math.Min( frames - written, stagedFrames - stagedTaken );

			for ( var frame = 0; frame < take; frame++ )
			{
				for ( var channel = 0; channel < channels; channel++ )
				{
					var sample = channel < stagedChannels
						? staged[( ( stagedTaken + frame ) * stagedChannels ) + channel]
						: 0.0f;

					output[( ( written + frame ) * channels ) + channel] = sample;
				}
			}

			written += take;
			stagedTaken += take;
		}

		//Whatever is left over is silence: a device given a short buffer plays whatever was in the memory, which is a burst of noise.
		for ( var frame = written; frame < frames; frame++ )
		{
			for ( var channel = 0; channel < channels; channel++ )
			{
				output[( frame * channels ) + channel] = 0.0f;
			}
		}
	}

	//What the pump calls when the device wants more. On the audio thread, and never allowed to throw: an exception crossing back
	//into native code takes the process with it, and a mixer that failed is a reason for silence rather than for the game to stop.
	[UnmanagedCallersOnly]
	private static void Render( void* user, float* samples, int frames, int channels )
	{
		if ( samples is null || frames <= 0 || channels <= 0 ) return;

		var device = current;

		if ( device is null )
		{
			Unsafe.InitBlockUnaligned( samples, 0, (uint)( frames * channels * sizeof( float ) ) );
			return;
		}

		try
		{
			device.Fill( samples, frames, channels );
		}
		catch ( Exception )
		{
			Unsafe.InitBlockUnaligned( samples, 0, (uint)( frames * channels * sizeof( float ) ) );
		}
	}

	public void Tick()
	{
		//Nothing: the device runs itself, and what it plays is decided when it asks rather than when a frame goes past.
	}

	public void Shutdown()
	{
		//Let go while away is still open as far as everything else is concerned.
		var wasSuspended = suspended;
		suspended = false;

		if ( pump is null && !wasSuspended ) return;

		//Closing waits for the last callback to finish, so nothing is left running into managed state that is on its way out.
		AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;

		pump?.Dispose();
		pump = null;

		MixingThread.DrivenByOurDevice = false;

		if ( ReferenceEquals( current, this ) ) current = null;
	}
}
