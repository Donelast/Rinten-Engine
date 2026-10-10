namespace Rinten.Utility;

//1. Be as fast as possible to iterate 2. Queue modifications while iterating 3. Don't
//allow duplicate entries 4. Fast as possible removal 5. Thread concurrency doesn't matter

//Wrapper around a HashSet that supports items being added / removed during enumeration. Enumerate the set with EnumerateLocked.
internal class HashSetEx<T> : IHotloadManaged
{
	[SuppressNullKeyWarning]
	private readonly HashSet<T> _hashset = new( 16 );
	private readonly List<T> _cachedList = new( 16 );

	private bool _listInvalid;
	private int _activeEnumerators;

	/// <summary>Current number of unique items in the set.</summary>
	public int Count => _hashset.Count;

	/// <summary>List view of the set. This is only updated when there are no active enumerators created by <see cref="EnumerateLocked"/>.</summary>
	public IReadOnlyList<T> List
	{
		get
		{
			UpdateList();
			return _cachedList;
		}
	}

	/// <summary>Adds an item to the set, returning true if it wasn't already present. If any enumerators are active, they won't see this new item yet.</summary>
	public bool Add( T obj )
	{
		if ( !_hashset.Add( obj ) ) return false;

		_listInvalid = true;
		return true;
	}

	/// <summary>Removes an item from the set, returning true if it was present. If any enumerators are active, they will still see the removed item.</summary>
	public bool Remove( T obj )
	{
		if ( !_hashset.Remove( obj ) ) return false;

		_listInvalid = true;
		return true;
	}

	/// <summary>Determines whether this set contains the given object.</summary>
	public bool Contains( T obj ) => _hashset.Contains( obj );

	/// <summary>Remove all items from the set. If any enumerators are active, they won't be affected.</summary>
	public void Clear()
	{
		if ( _hashset.Count <= 0 ) return;

		_hashset.Clear();
		_listInvalid = true;
	}

	//If any items were added / removed, and there are no active enumerators, synchronize _cachedList with items from _hashset.
	private void UpdateList()
	{
		if ( !_listInvalid ) return;
		if ( _activeEnumerators > 0 ) return;

		_listInvalid = false;

		_cachedList.Clear();
		_cachedList.AddRange( _hashset );
	}

	/// <summary>Allocation-free walk that allows edits mid-iteration; use directly in foreach, never as IEnumerable</summary>
	public Locked EnumerateLocked( bool nullChecks = false ) => new( this, nullChecks );

	/// <summary>What <see cref="EnumerateLocked"/> hands back. Only <c>foreach</c> reads it.</summary>
	public readonly struct Locked
	{
		private readonly HashSetEx<T> set;
		private readonly bool nullChecks;

		internal Locked( HashSetEx<T> set, bool nullChecks )
		{
			this.set = set;
			this.nullChecks = nullChecks;
		}

		public Enumerator GetEnumerator() => new( set, nullChecks );
	}

	/// <summary>The walk itself. Holds the set open - <see cref="UpdateList"/> refuses to rebuild the list while any of these is alive, which is what makes adding and removing during a walk safe.</summary>
	public struct Enumerator : IDisposable
	{
		private readonly HashSetEx<T> set;
		private readonly bool nullChecks;
		private int index;
		private bool open;

		internal Enumerator( HashSetEx<T> set, bool nullChecks )
		{
			this.set = set;
			this.nullChecks = nullChecks;
			index = 0;
			Current = default;

			set.UpdateList();
			set._activeEnumerators++;
			open = true;
		}

		public T Current { get; private set; }

		public bool MoveNext()
		{
			while ( index < set._cachedList.Count )
			{
				var item = set._cachedList[index++];

				if ( nullChecks && item is IValid { IsValid: false } )
				{
					set.Remove( item );
					continue;
				}

				Current = item;
				return true;
			}

			return false;
		}

		/// <summary>Lets the set rebuild its list again. Guarded because a struct enumerator can be copied, and two copies disposing would take the count below zero and leave the list frozen for the rest of the process.</summary>
		public void Dispose()
		{
			if ( !open ) return;

			open = false;
			set._activeEnumerators--;
		}
	}

	//If types are removed during hotload, items of those types are automatically removed from
	//_hashset because it can't contain null items. Therefore, we'll need to rebuild _cachedList too.

	void IHotloadManaged.Created( IReadOnlyDictionary<string, object> state ) => _listInvalid = true;
	void IHotloadManaged.Persisted() => _listInvalid = true;
}
