namespace Rinten;

//Per-part indirect args with the instance count copied in on the GPU; double-buffered by frame
internal static class CoreInstanceArgs
{
	//An indexed draw's arguments: five words.
	private const long Bytes = 20;

	private static ulong buffer;

	//How many bytes one half holds.
	private static long half;

	private static long used;
	private static long frameAt = -1;
	private static bool saidPortable;

	//Writes draw args with the instance count copied from countFrom, outside passes; false on portable
	internal static bool Put( bool indexed, int count, int first, int baseVertex, ulong countFrom, long countAt,
		out ulong into, out long at )
	{
		into = 0;
		at = 0;

		if ( countFrom == 0 || countAt < 0 || count <= 0 ) return false;

		//The portable frame writes no buffer where it is and copies none - see CoreRender.FrameWrite - so a count the card holds cannot reach a draw there.
		if ( CoreRenderDevice.PortableFrame )
		{
			if ( !saidPortable )
			{
				saidPortable = true;
				Log.Warning( "a model drawn as many times as a buffer says is not drawn in the portable frame, which copies nothing on the card" );
			}

			return false;
		}

		var frame = EngineLoop.LoopFrames;

		if ( frame != frameAt )
		{
			frameAt = frame;
			used = 0;
		}

		if ( buffer == 0 || used + Bytes > half )
		{
			var wanted = Math.Max( half * 2, Bytes * 64 );
			var made = CoreRenderDevice.ReserveBuffer( wanted * 2 );

			if ( made == 0 ) return false;

			//Draws recorded earlier this frame name the old one; it goes to the graveyard and outlives them.
			if ( buffer != 0 ) CoreRenderDevice.DestroyBuffer( buffer );

			buffer = made;
			half = wanted;
		}

		at = ( ( frame & 1 ) * half ) + used;
		used += Bytes;

		Span<uint> words = stackalloc uint[5];

		words[0] = (uint)count;
		words[1] = 0;
		words[2] = (uint)Math.Max( first, 0 );
		words[3] = indexed ? unchecked( (uint)baseVertex ) : 0u;
		words[4] = 0;

		if ( !CoreRenderDevice.FrameWrite( buffer, at, System.Runtime.InteropServices.MemoryMarshal.AsBytes( words ) ) ) return false;
		if ( !CoreRenderDevice.FrameCopy( countFrom, countAt + 4, buffer, at + 4, 4 ) ) return false;

		into = buffer;

		return true;
	}

	//Let go with the device's resources - see CoreSceneDrawer.ReleaseResources.
	internal static void Release()
	{
		if ( buffer != 0 ) CoreRenderDevice.DestroyBuffer( buffer );

		buffer = 0;
		half = 0;
		used = 0;
		frameAt = -1;
	}
}
