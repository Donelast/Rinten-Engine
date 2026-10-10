using System.Threading;

namespace Rinten.Utility;

/// <summary>Wrappers of the parallel class.</summary>
public static class Parallel
{
	//This is all wrapped so that if it turns out to be a bad idea to expose it, we can do an update where this just does a regular foreach.

	//Whether there are threads to fan out to. A page has one (plan В5): there a parallel loop is only its machinery -
	//partitioners, tasks, closures, the most of a particle scene's garbage at 1.8 MB a frame - around the same loop run in order.
	internal static bool InOrder => OperatingSystem.IsBrowser();

	public static bool ForEach<T>( IEnumerable<T> source, Action<T> body )
	{
		if ( InOrder )
		{
			if ( source is IList<T> list )
			{
				for ( int i = 0; i < list.Count; i++ ) body( list[i] );
			}
			else
			{
				foreach ( var item in source ) body( item );
			}

			return true;
		}

		var r = System.Threading.Tasks.Parallel.ForEach( source, body );
		return r.IsCompleted;
	}

	public static bool ForEach<T>( IEnumerable<T> source, CancellationToken token, Action<T> body )
	{
		if ( InOrder )
		{
			foreach ( var item in source )
			{
				if ( token.IsCancellationRequested ) return false;
				body( item );
			}

			return true;
		}

		var r = System.Threading.Tasks.Parallel.ForEach( source, new ParallelOptions { CancellationToken = token }, body );
		return r.IsCompleted;
	}

	public static bool For( int fromInclusive, int toExclusive, Action<int> body )
	{
		if ( InOrder )
		{
			for ( int i = fromInclusive; i < toExclusive; i++ ) body( i );
			return true;
		}

		var r = System.Threading.Tasks.Parallel.For( fromInclusive, toExclusive, body );
		return r.IsCompleted;
	}

	public static async Task ForAsync( int fromInclusive, int toExclusive, CancellationToken token, Func<int, CancellationToken, ValueTask> body )
	{
		await System.Threading.Tasks.Parallel.ForAsync( fromInclusive, toExclusive, new ParallelOptions { CancellationToken = token }, body );
	}
}
