using System.Threading;

namespace Rinten;

//Waits for a resource to finish loading. A manifest was how the legacy resource system streamed a file in, and this was the handle on
//one. Ours reads the bytes itself, so a resource is loaded by the time anything can ask about it and there is nothing to wait for.
class AsyncResourceLoader : IDisposable
{
	public Task WaitForLoad( CancellationToken token = default ) => Task.CompletedTask;

	public void Dispose()
	{
	}

	static public AsyncResourceLoader Load( string filename ) => null;
}
