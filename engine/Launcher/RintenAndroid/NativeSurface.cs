using System;
using System.Runtime.InteropServices;

namespace Rinten.Android;

//The window behind a Java surface, which is what Vulkan draws into. Android hands an activity a Surface , and Vulkan wants an ANativeWindow*
//. The two are the same thing on either side of the JNI boundary, and this is the call that crosses it - so no C++ of ours has to.
internal static class NativeSurface
{
	[DllImport( "android", EntryPoint = "ANativeWindow_fromSurface" )]
	private static extern IntPtr FromSurface( IntPtr env, IntPtr surface );

	[DllImport( "android", EntryPoint = "ANativeWindow_release" )]
	private static extern void Release( IntPtr window );

	//A reference of our own, held until Let.
	public static IntPtr Take( global::Android.Views.Surface surface )
	{
		if ( surface is null || surface.Handle == IntPtr.Zero ) return IntPtr.Zero;

		return FromSurface( Java.Interop.JniEnvironment.EnvironmentPointer, surface.Handle );
	}

	public static void Let( IntPtr window )
	{
		if ( window != IntPtr.Zero ) Release( window );
	}
}
