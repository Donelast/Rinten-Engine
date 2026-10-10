namespace Rinten;

//Records GPU work requested outside a frame into a tiny 1x1 view frame of its own
internal static class CoreGpuWork
{
	private static ulong view;

	//Somewhere open to record into: the frame being recorded, or a frame of this class's own, which
	//is what own says. False where there is neither - no device, or a device that has no views.
	internal static bool Begin( out bool own )
	{
		own = false;

		if ( !CoreRenderDevice.IsReady ) return false;
		if ( CoreRenderDevice.Recording ) return true;

		if ( view == 0 ) view = CoreRenderDevice.CreateView( 1, 1 );
		if ( view == 0 ) return false;

		if ( !CoreRenderDevice.BeginViewFrame( view ) ) return false;

		own = true;

		return true;
	}

	//Closes what Begin opened, where it was this class's own: sent at once, and not waited for - a read
	//back that has to have what the work wrote waits for the device itself, see GpuBuffer.GetData.
	internal static void End( bool own )
	{
		if ( own ) CoreRenderDevice.EndViewFrameAsync( view );
	}

	//Let go with the device's resources - see CoreSceneDrawer.ReleaseResources.
	internal static void Release()
	{
		if ( view != 0 ) CoreRenderDevice.DestroyView( view );

		view = 0;
	}
}
