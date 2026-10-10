using System.Collections.Concurrent;

namespace Rinten.Internal;

public partial class TypeLibrary
{
	private readonly ConcurrentDictionary<int, object> _cacheDictionary = new();

	//ConcurrentDictionary does not allow null values, so we box null results with this sentinel.
	private static readonly object NullSentinel = new();

	//Get or cache the result
	T Cached<T>( int key, Func<T> factory )
	{
		//GetOrAdd is thread-safe; the factory may be called more than once under contention,
		//but only one result is stored and all callers receive the same stored value.
		var result = _cacheDictionary.GetOrAdd( key, _ =>
		{
			var val = factory();
			return val is null ? NullSentinel : (object)val;
		} );

		return result == NullSentinel ? default! : (T)result;
	}

	//Clear the cache. Should be called when types are added or removed.
	void InvalidateCache()
	{
		_cacheDictionary.Clear();
	}
}
