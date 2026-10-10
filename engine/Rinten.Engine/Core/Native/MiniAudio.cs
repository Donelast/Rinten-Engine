using System.Runtime.InteropServices;

namespace Rinten.Native;

//miniaudio and stb_vorbis via audio_shim.c, which holds version-specific structs as opaque pointers
internal static unsafe partial class MiniAudio
{
	private const string Library = "rintencore";

	//-- devices --------------------------------------------------------------

	//The system's own device.
	internal const int DeviceDefault = 0;

	//A device that goes nowhere, keeping its own clock. What a machine with no sound card gets, what a
	//server runs, and what a test watches: it asks for samples at the right rate and throws them away.
	internal const int DeviceNull = 1;

	//Opens a device without starting it. What it decided about rate and channels
	//comes back through the out parameters; the device's name is copied into name.
	internal static void* rn_audio_device_open( int kind, int capture, int sampleRate, int channels,
		int framesPerBuffer, delegate* unmanaged[Cdecl]<void*, float*, float*, uint, void> callback, void* user,
		int* outSampleRate, int* outChannels, byte* name, int nameCapacity )
		=> DeviceOpen( kind, capture, sampleRate, channels, framesPerBuffer, (void*)callback, user,
			outSampleRate, outChannels, name, nameCapacity );

	//The callback crosses as the pointer it is: a browser's runtime cannot call a native
	//function whose signature names a function pointer type - see the same in Box3D.g.cs.
	[LibraryImport( Library, EntryPoint = "rn_audio_device_open" )]
	private static partial void* DeviceOpen( int kind, int capture, int sampleRate, int channels,
		int framesPerBuffer, void* callback, void* user,
		int* outSampleRate, int* outChannels, byte* name, int nameCapacity );

	[LibraryImport( Library )]
	internal static partial int rn_audio_device_start( void* device );

	//Stops, waits for the last callback to leave, and frees.
	[LibraryImport( Library )]
	internal static partial void rn_audio_device_close( void* device );

	//-- decoders -------------------------------------------------------------

	//A decoder over bytes the caller keeps alive and in place for as long as it lives. Zero channels and rate mean the file's own.
	[LibraryImport( Library )]
	internal static partial void* rn_decoder_open( void* data, nuint size, int channels, int sampleRate,
		int* outChannels, int* outSampleRate, ulong* outFrames );

	//Frames read; zero at the end or on an error.
	[LibraryImport( Library )]
	internal static partial ulong rn_decoder_read( void* decoder, float* output, ulong frames );

	[LibraryImport( Library )]
	internal static partial int rn_decoder_seek( void* decoder, ulong frame );

	[LibraryImport( Library )]
	internal static partial void rn_decoder_close( void* decoder );

	//-- resamplers -----------------------------------------------------------

	[LibraryImport( Library )]
	internal static partial void* rn_resampler_open( int channels, int inRate, int outRate );

	[LibraryImport( Library )]
	internal static partial int rn_resampler_set_rate( void* resampler, int inRate, int outRate );

	[LibraryImport( Library )]
	internal static partial void rn_resampler_reset( void* resampler );

	[LibraryImport( Library )]
	internal static partial int rn_resampler_process( void* resampler, float* input, ulong* inFrames,
		float* output, ulong* outFrames );

	[LibraryImport( Library )]
	internal static partial void rn_resampler_close( void* resampler );

	//-- ogg ------------------------------------------------------------------

	//Decodes an ogg whole into 16 bit interleaved samples that rn_vorbis_free gives back. Frames, or no more than zero when it is not one.
	[LibraryImport( Library )]
	internal static partial int rn_vorbis_decode( void* data, int size, int* outChannels, int* outSampleRate, short** outSamples );

	[LibraryImport( Library )]
	internal static partial void rn_vorbis_free( short* samples );
}
