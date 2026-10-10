namespace Rinten;

partial class CoreWebXr
{
	//Reads the whole headset state for the tick; no wait, since the page's XR callback already paced it
	internal static void FrameStart()
	{
		var was = live;

		Read();

		//The session ended - the player took the headset off, or pressed the system's way out. The eyes' views and
		//their drawers go with it: nothing else ever lets go of them, and a session opened again is a new size.
		if ( was && !live ) Release();
	}

	//The end of the engine's frame: each eye drawn from the scene's camera into the part of the headset's framebuffer the browser gave it. There is
	//no frame to hand back. A browser takes what is in its framebuffer when the animation callback returns, so drawing the eyes is the whole of it.
	internal static void FrameEnd( Scene scene )
	{
		if ( !live ) return;

		DrawEyes( scene );
	}
}
