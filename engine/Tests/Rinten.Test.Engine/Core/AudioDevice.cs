using System;
using System.Runtime.InteropServices;
using System.Threading;
using Rinten.Audio;

namespace EngineTests;

//The audio output device: the last part of sound to move into the engine's own core. Tested against a device that goes nowhere - it keeps its own clock and
//asks for samples at the right rate, so everything about the pump can be watched on a machine with no speaker, which is what a test host is.
[TestClass]
public class AudioDeviceTest
{
	private static int calls;
	private static long framesAsked;
	private static int channelsAsked;

	//Stands in for the mixer: counts what it was asked for and writes silence.
	[UnmanagedCallersOnly]
	private static unsafe void Render( void* user, float* samples, int frames, int channels )
	{
		Interlocked.Increment( ref calls );
		Interlocked.Add( ref framesAsked, frames );
		Volatile.Write( ref channelsAsked, channels );

		for ( var i = 0; i < frames * channels; i++ ) samples[i] = 0.0f;
	}

	private static bool Ready()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return false;
		}

		return true;
	}

	//Wait for the device to have asked for something.
	private static bool Pumped( CoreAudioPump device, int atLeast )
	{
		for ( var attempt = 0; attempt < 100; attempt++ )
		{
			if ( device.FramesRendered >= (ulong)atLeast ) return true;

			Thread.Sleep( 10 );
		}

		return false;
	}

	[TestMethod]
	public unsafe void DeviceAsksForSamplesUntilItIsClosed()
	{
		if ( !Ready() ) return;

		calls = 0;
		framesAsked = 0;
		channelsAsked = 0;

		var device = CoreAudioPump.Open( CoreAudioDevice.DeviceNull, 44100, 2, 512, &Render, null );

		Assert.IsNotNull( device, "a device that goes nowhere should always open" );

		try
		{
			Assert.AreEqual( 44100, device.SampleRate );
			Assert.AreEqual( 2, device.ChannelCount );

			Assert.IsTrue( Pumped( device, 4096 ), "the device should be asking for samples on its own" );

			Assert.IsTrue( Volatile.Read( ref calls ) > 0, "and asking us for them" );
			Assert.AreEqual( 2, Volatile.Read( ref channelsAsked ), "for as many channels as it has" );

			Assert.IsTrue( Interlocked.Read( ref framesAsked ) > 0 );
		}
		finally
		{
			device.Dispose();
		}

		//Closing waits for the last callback: nothing may run afterwards, or the mixer would be reached into after it was taken apart.
		var after = Volatile.Read( ref calls );
		Thread.Sleep( 50 );

		Assert.AreEqual( after, Volatile.Read( ref calls ), "a closed device asks for nothing" );
	}

	//The whole chain: our device asks, the managed mixer runs, and what it made comes back to the device. The mix happens on the device's thread,
	//as before - and while ours is playing, the native device must not be pulling the same graph. That is what the flag here is.
	[TestMethod]
	public void OurDeviceDrivesTheMixer()
	{
		if ( !Ready() ) return;

		var device = new CoreAudioDevice( CoreAudioDevice.DeviceNull );

		Assert.IsTrue( device.IsValid, "a device that goes nowhere should always open" );

		try
		{
			Assert.IsTrue( Rinten.Audio.MixingThread.DrivenByOurDevice,
				"s&box's device must stop pulling the mixer while ours is" );

			//Installed as the one in use, so the end of a mix comes back here rather than to whichever device the process opened first.
			Rinten.Audio.EngineAudio.Reset( device );

			//A test host never starts the engine, so there is no mixer graph until one is asked for. Nothing is playing
			//through it - what is being watched is that the mix happens at all, and that what it makes reaches the device.
			Rinten.Audio.Mixer.ResetToDefault();

			for ( var attempt = 0; attempt < 100 && device.BuffersMixed == 0; attempt++ )
			{
				Thread.Sleep( 10 );
			}

			Assert.IsTrue( device.BuffersMixed > 0, "the device should have had the mixer run for it" );
			Assert.IsTrue( device.FramesRendered > 0 );
		}
		finally
		{
			Rinten.Audio.EngineAudio.Reset();
		}

		Assert.IsFalse( Rinten.Audio.MixingThread.DrivenByOurDevice,
			"and hand the mixer back when it goes" );
	}

	//Muting silences without stopping - the pump keeps its clock, so unmuting is immediate rather than the length of opening a device.
	[TestMethod]
	public unsafe void MutingKeepsThePumpRunning()
	{
		if ( !Ready() ) return;

		calls = 0;

		var device = CoreAudioPump.Open( CoreAudioDevice.DeviceNull, 44100, 2, 512, &Render, null );
		Assert.IsNotNull( device );

		try
		{
			Assert.IsTrue( Pumped( device, 2048 ) );

			device.Muted = true;
			Assert.IsTrue( device.Muted );

			//Wait for several quiet polls: the mute flag is lock-free, so an in-flight buffer still plays
			var whenMuted = Volatile.Read( ref calls );
			var quiet = 0;

			for ( var attempt = 0; attempt < 100 && quiet < 5; attempt++ )
			{
				Thread.Sleep( 20 );

				var now = Volatile.Read( ref calls );

				quiet = now == whenMuted ? quiet + 1 : 0;
				whenMuted = now;
			}

			Assert.AreEqual( 5, quiet, "a muted device should have stopped asking" );

			var framesWhenMuted = device.FramesRendered;

			Thread.Sleep( 100 );

			Assert.IsTrue( device.FramesRendered > framesWhenMuted,
				"a muted device is still running" );

			Assert.AreEqual( whenMuted, Volatile.Read( ref calls ),
				"but nothing is asked to make the sound it is not playing" );

			device.Muted = false;
			Thread.Sleep( 50 );

			Assert.IsTrue( Volatile.Read( ref calls ) > whenMuted, "and unmuting picks it straight back up" );
		}
		finally
		{
			device.Dispose();
		}
	}
}
