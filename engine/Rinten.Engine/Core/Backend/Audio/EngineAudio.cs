namespace Rinten.Audio;

//Which device the sound comes out of. Chosen once, when the first sound is mixed, and kept: an output device is not something to swap under a
//running mixer. Our own unless it will not open or the backend was pinned, in which case the legacy one - the same rule every other subsystem follows.
internal static class EngineAudio
{
	private static readonly Logger log = new( "Audio" );

	private static IAudioDeviceBackend device;

	//The device in use, opening one if none has been.
	internal static IAudioDeviceBackend Device => device ??= Open();

	private static IAudioDeviceBackend Open()
	{

		//Nothing is listening, and nothing should be running: a device pulls the mixer on a thread of its own, and a host that never started
		//the engine has that thread reaching into a graph that things are being taken out of. A test that wants a real device opens one itself.
		if ( Application.IsUnitTest || Application.IsHeadless ) return new SilentAudioDevice();

		//In a page too: the core is linked into the page's module, and miniaudio plays through the browser's
		//WebAudio there - the callback that pulls the mixer comes on the page's one thread, between frames.
		var ours = new CoreAudioDevice();
		if ( ours.IsValid ) return ours;

		//Nothing opened - a machine with no sound, or a core that is not there. Silence is better coming from something that says so.
		log.Info( "our audio device did not open, playing into nothing" );

		return new SilentAudioDevice();
	}

	//Lets the output device go while the application is in the background, or takes it
	//back - see CoreAudioDevice.Suspend. Nothing where no device has been opened yet.
	internal static void Suspend( bool suspend )
	{
		if ( device is CoreAudioDevice ours ) ours.Suspend( suspend );
	}

	//Whether anything is being played into.
	internal static bool IsValid => Device.IsValid;

	//Somewhere for sound to go when there is nowhere for it to go. A server, or a test host. The mixer above still works - a game asking
	//whether a sound is playing gets a real answer - and nothing pulls it, so nothing runs on a thread that host does not know about.
	private sealed class SilentAudioDevice : IAudioDeviceBackend
	{
		public bool IsValid => true;

		public string Name => "none";

		public int ChannelCount => 2;

		public int SampleRate => (int)AudioEngine.SamplingRate;

		public bool Muted { get; set; }

		public void Clear() { }

		public void Submit( MultiChannelBuffer buffer ) { }

		public void Tick() { }

		public void Shutdown() { }
	}

	//Put the device back, for a test that wants one of its own.
	internal static void Reset( IAudioDeviceBackend replacement = null )
	{
		device?.Shutdown();
		device = replacement;
	}
}
