namespace Rinten;

//Async GPU buffer readbacks (GpuBuffer.GetDataAsync), polled each loop; callbacks on the main thread
internal static class CoreReadbacks
{
	private abstract class Request
	{
		public int Id;
		public int Loops;

		/// <summary>Hands the bytes to whoever asked, as what they asked for.</summary>
		public abstract void Deliver( ReadOnlySpan<byte> bytes );
	}

	private sealed class Request<T> : Request where T : unmanaged
	{
		public Action<ReadOnlySpan<T>> Callback;

		public override void Deliver( ReadOnlySpan<byte> bytes )
			=> Callback( System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>( bytes ) );
	}

	private static readonly List<Request> waiting = new();
	private static readonly List<Request> arrived = new();

	//How many loops a request waits for a frame before it is given one.
	private const int LoopsBeforeOwnFrame = 2;

	//How many loops a submitted request waits before the device is waited for.
	private const int LoopsBeforeWait = 8;

	//A run of a buffer asked back, for callback on the main thread once it arrives. False where the device will not read anything back.
	internal static bool Ask<T>( ulong buffer, long offset, long bytes, Action<ReadOnlySpan<T>> callback ) where T : unmanaged
	{
		if ( callback is null || buffer == 0 || bytes <= 0 ) return false;

		var id = CoreRenderDevice.AskReadback( buffer, offset, bytes );
		if ( id == 0 ) return false;

		lock ( waiting )
		{
			waiting.Add( new Request<T> { Id = id, Callback = callback } );
		}

		return true;
	}

	//Once a loop, on the main thread: every request that has arrived handed over, and the ones that are stuck moved on.
	internal static void Pump()
	{
		var giveFrame = false;
		var wait = false;

		lock ( waiting )
		{
			if ( waiting.Count == 0 ) return;

			for ( var i = waiting.Count - 1; i >= 0; i-- )
			{
				var request = waiting[i];
				var state = CoreRenderDevice.PollReadback( request.Id, out _, out var recorded );

				if ( state != 0 )
				{
					arrived.Add( request );
					waiting.RemoveAt( i );
					continue;
				}

				request.Loops++;

				if ( !recorded && request.Loops >= LoopsBeforeOwnFrame ) giveFrame = true;
				if ( recorded && request.Loops >= LoopsBeforeWait ) wait = true;
			}
		}

		//Out from under the lock: a callback may well ask for another.
		foreach ( var request in arrived ) Hand( request );

		arrived.Clear();

		if ( giveFrame && !CoreRenderDevice.Recording && CoreGpuWork.Begin( out var own ) ) CoreGpuWork.End( own );

		if ( wait ) CoreRenderDevice.WaitIdle();
	}

	private static void Hand( Request request )
	{
		try
		{
			var state = CoreRenderDevice.PollReadback( request.Id, out var bytes, out _ );

			if ( state == 1 ) request.Deliver( bytes );
			else Log.Warning( "a buffer asked back with GetDataAsync went before it could be read" );
		}
		catch ( Exception e )
		{
			Log.Warning( e, "a GetDataAsync callback threw" );
		}
		finally
		{
			CoreRenderDevice.ReleaseReadback( request.Id );
		}
	}
}
