using System.Collections;
namespace Rinten;

/// <summary>An ordered collection of unique objects with add/remove callbacks. Maintains insertion order and provides change notifications.</summary>
public class SelectionSystem : IEnumerable<object>
{
	//Value is always false and unused - we only need the ordered key collection
	private readonly OrderedDictionary<object, bool> _list = new();

	//Random hash code that changes when the collection is modified
	int _hashCode = Random.Shared.Int( 0, 4096 );

	/// <summary>Invoked when an item is added to the selection.</summary>
	public Action<object> OnItemAdded { get; set; }

	/// <summary>Invoked when an item is removed from the selection.</summary>
	public Action<object> OnItemRemoved { get; set; }

	/// <summary>Gets a hash code that changes whenever the collection is modified. Useful for detecting selection changes.</summary>
	public override int GetHashCode() => _hashCode;

	//The struct, so a foreach over the selection allocates nothing: through the interface every walk boxed the dictionary's enumerator, and the scene view walks the selection several times a frame.
	/// <summary>Returns an enumerator that iterates through the selected objects in order.</summary>
	public Enumerator GetEnumerator() => new( _list.Keys.GetEnumerator() );

	IEnumerator<object> IEnumerable<object>.GetEnumerator() => GetEnumerator();

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	/// <summary>Walks the selection, in the order it was made.</summary>
	public struct Enumerator : IEnumerator<object>
	{
		private OrderedDictionary<object, bool>.KeyCollection.Enumerator inner;

		internal Enumerator( OrderedDictionary<object, bool>.KeyCollection.Enumerator inner ) => this.inner = inner;

		public object Current => inner.Current;

		public bool MoveNext() => inner.MoveNext();

		//The dictionary's key enumerator holds nothing to let go of.
		public readonly void Dispose() { }

		void IEnumerator.Reset() => throw new NotSupportedException();
	}

	/// <summary>Gets the number of selected objects.</summary>
	public int Count => _list.Count;

	/// <summary>Removes all objects from the selection, invoking OnItemRemoved for each.</summary>
	public virtual void Clear()
	{
		if ( !_list.Any() )
			return;

		foreach ( var kvp in _list )
		{
			OnItemRemoved?.Invoke( kvp.Key );
		}

		_list.Clear();
		_hashCode++;
	}

	/// <summary>Adds an object to the selection.</summary>
	public virtual bool Add( object obj )
	{
		if ( obj is null )
			return false;

		if ( _list.TryAdd( obj, false ) )
		{
			_hashCode++;
			OnItemAdded?.Invoke( obj );
			return true;
		}

		return false;
	}

	/// <summary>Clears the selection and sets it to a single object.</summary>
	public virtual bool Set( object obj )
	{
		if ( _list.Count == 1 && _list.Keys.First() == obj )
			return false;

		Clear();
		Add( obj );
		return true;
	}

	/// <summary>Removes an object from the selection.</summary>
	public virtual bool Remove( object obj )
	{
		if ( obj is null )
			return false;

		if ( _list.Remove( obj ) )
		{
			_hashCode++;
			OnItemRemoved?.Invoke( obj );
			return true;
		}

		return false;
	}

	/// <summary>Checks if an object is in the selection.</summary>
	public virtual bool Contains( object obj )
	{
		if ( obj is null ) return false;
		return _list.ContainsKey( obj );
	}

	/// <summary>Checks if the selection contains any objects.</summary>
	public virtual bool Any() => _list.Count != 0;
}
