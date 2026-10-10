namespace Rinten.Audio;

//Plays the mixer's output; the device pulls on its own thread, so answers must never block
internal interface IAudioDeviceBackend
{
	//Whether there is anything to play into.
	bool IsValid { get; }

	//What the system calls it.
	string Name { get; }

	//How many speakers. Two, unless somebody has more.
	int ChannelCount { get; }

	//Samples a second. The mixer is built around this not changing.
	int SampleRate { get; }

	//Silence without stopping. The pump keeps running and the samples are dropped: stopping
	//a device and starting it again takes long enough to be heard, and muting should not be.
	bool Muted { get; set; }

	//Throw away whatever has been handed over and not yet played.
	void Clear();

	//One buffer of mixed audio, ready to play. Called from inside the mixer, at the end of a mix, on whichever thread did
	//the mixing - which for our own device is the device's own thread, because the mix happens when the device asks for it.
	void Submit( MultiChannelBuffer buffer );

	//Called once a frame from the main thread.
	void Tick();

	//Stop it and let it go.
	void Shutdown();
}
