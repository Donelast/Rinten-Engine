using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Rinten;

/// <summary>Describes a change to a <see cref="NetListChangeEvent{T}"/> which is passed to <see cref="NetList{T}.OnChanged"/> whenever its contents change.</summary>
public struct NetListChangeEvent<T>
{
	public NotifyCollectionChangedAction Type { get; set; }
	public int Index { get; set; }
	public int MovedIndex { get; set; }
	public T NewValue { get; set; }
	public T OldValue { get; set; }
}

/// <summary>A networkable list for use with the SyncAttribute and HostSyncAttribute. Only changes will be networked instead of sending the whole list every time, so it's more efficient. Example usage: public class MyComponent : Component { [Sync] public NetList&lt;int&gt; MyIntegerList { get; set; } = new(); public void AddNumber( int number ) { if ( IsProxy ) return; MyIntegerList.Add( number ); } }</summary>
public sealed class NetList<T> : INetworkSerializer, INetworkReliable, INetworkProperty, IDisposable, IList, IList<T>, IReadOnlyList<T>
{
	//Represents a change in the list.
	private struct Change
	{
		public NotifyCollectionChangedAction Type { get; set; }
		public int Index { get; set; }
		public int MovedIndex { get; set; }
		public T Value { get; set; }
	}

	/// <summary>Get notified when the list has changed.</summary>
	public Action<NetListChangeEvent<T>> OnChanged;

	private readonly ObservableCollection<T> _list = new();
	private readonly List<Change> _changes = new();

	public NetList()
	{
		_list.CollectionChanged += OnCollectionChanged;
		AddResetChange();
	}

	public void Dispose()
	{
		_changes.Clear();
	}

	bool ICollection<T>.IsReadOnly => false;
	bool IList.IsReadOnly => false;
	bool IList.IsFixedSize => false;
	bool ICollection.IsSynchronized => false;
	object ICollection.SyncRoot => this;

	object IList.this[int index]
	{
		get => this[index];
		set => this[index] = (T)value;
	}

	int IList.Add( object value )
	{
		Add( (T)value );
		return Count - 1;
	}

	bool ICollection<T>.Remove( T item )
	{
		return Remove( item );
	}

	void ICollection.CopyTo( Array array, int index )
	{
		(_list as ICollection).CopyTo( array, index );
	}

	bool IList.Contains( object value )
	{
		return _list.Contains( (T)value );
	}

	int IList.IndexOf( object value )
	{
		return _list.IndexOf( (T)value );
	}

	void IList.Insert( int index, object value )
	{
		Insert( index, (T)value );
	}

	void IList.Remove( object value )
	{
		Remove( (T)value );
	}

	public void Clear()
	{
		if ( !CanWriteChanges() )
			return;

		_list.Clear();
	}

	public bool Contains( T item )
	{
		return _list.Contains( item );
	}

	public void CopyTo( T[] array, int arrayIndex )
	{
		_list.CopyTo( array, arrayIndex );
	}

	public void Add( T value )
	{
		if ( !CanWriteChanges() )
			return;

		_list.Add( value );
	}

	public void AddRange( IEnumerable<T> collection )
	{
		if ( !CanWriteChanges() )
			return;

		foreach ( var value in collection )
		{
			_list.Add( value );
		}
	}

	public bool Remove( T value )
	{
		return CanWriteChanges() && _list.Remove( value );
	}

	public int IndexOf( T item )
	{
		return _list.IndexOf( item );
	}

	public void Insert( int index, T value )
	{
		if ( !CanWriteChanges() )
			return;

		_list.Insert( index, value );
	}

	public void RemoveAt( int index )
	{
		if ( !CanWriteChanges() )
			return;

		_list.RemoveAt( index );
	}

	public int Count => _list.Count;

	public T this[int key]
	{
		get
		{
			return _list[key];
		}
		set
		{
			if ( !CanWriteChanges() )
				return;

			_list[key] = value;
		}
	}

	public IEnumerator<T> GetEnumerator()
	{
		return _list.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return ((IEnumerable)_list).GetEnumerator();
	}

	private INetworkProxy Parent { get; set; }

	void INetworkProperty.Init( int slot, INetworkProxy parent )
	{
		Parent = parent;
	}

	//Do we have any pending changes?
	bool INetworkSerializer.HasChanges => _changes.Count > 0;

	//Write any changed items to a ByteStream.
	void INetworkSerializer.WriteChanged( ref ByteStream data )
	{
		try
		{
			//We are sending changes, not a full update. This flag indicates that.
			data.Write( false );
			data.Write( _changes.Count );

			foreach ( var change in _changes )
			{
				data.Write( change.Type );
				data.Write( change.Index );
				data.Write( change.MovedIndex );
				WriteValue( change.Value, ref data );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when writing NetList changes - {e.Message}" );
		}

		_changes.Clear();
	}

	//Read a network update from a ByteStream.
	void INetworkSerializer.Read( ref ByteStream data )
	{
		try
		{
			var isFullUpdate = data.Read<bool>();

			if ( isFullUpdate )
				ReadAll( ref data );
			else
				ReadChanged( ref data );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when reading NetList - {e.Message}" );
		}

		//Clear changes whenever we read data. We don't want to keep local changes.
		_changes.Clear();
	}

	//Write all items to a ByteStream.
	void INetworkSerializer.WriteAll( ref ByteStream data )
	{
		try
		{
			//We are sending a full update. This flag indicates that.
			data.Write( true );
			data.Write( _list.Count );

			foreach ( var item in _list )
			{
				WriteValue( item, ref data );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when writing NetList - {e.Message}" );
		}
	}

	//Read all changes in the list as if we're building it for the first time.
	private void ReadAll( ref ByteStream data )
	{
		_list.Clear();

		var count = data.Read<int>();

		for ( var i = 0; i < count; i++ )
		{
			var value = ReadValue( ref data );
			_list.Add( value );
		}
	}

	//Read any changed items from a ByteStream.
	private void ReadChanged( ref ByteStream data )
	{
		var count = data.Read<int>();

		for ( var i = 0; i < count; i++ )
		{
			var type = data.Read<NotifyCollectionChangedAction>();
			var index = data.Read<int>();
			var movedIndex = data.Read<int>();
			var value = ReadValue( ref data );

			if ( type == NotifyCollectionChangedAction.Add )
			{
				if ( index >= 0 && index <= _list.Count )
					_list.Insert( index, value );
				else
					_list.Add( value );
			}
			else if ( type == NotifyCollectionChangedAction.Remove )
			{
				if ( index >= 0 && index < _list.Count )
				{
					var element = _list.ElementAt( index );
					_list.RemoveAt( index );
				}
			}
			else if ( type == NotifyCollectionChangedAction.Reset )
			{
				_list.Clear();
			}
			else if ( type == NotifyCollectionChangedAction.Replace )
			{
				if ( index >= 0 && index < _list.Count )
				{
					var element = _list.ElementAt( index );
				}

				_list[index] = value;
			}
			else if ( type == NotifyCollectionChangedAction.Move )
			{
				_list.Move( index, movedIndex );
			}
		}
	}

	private void OnCollectionChanged( object sender, NotifyCollectionChangedEventArgs e )
	{
		var changeEvent = new NetListChangeEvent<T>
		{
			Type = e.Action,
			Index = e.Action == NotifyCollectionChangedAction.Add ? e.NewStartingIndex : e.OldStartingIndex,
			MovedIndex = e.NewStartingIndex,
			OldValue = (e.OldItems is not null && e.OldItems.Count > 0) ? (T)e.OldItems[0] : default,
			NewValue = (e.NewItems is not null && e.NewItems.Count > 0) ? (T)e.NewItems[0] : default
		};

		OnChanged?.InvokeWithWarning( changeEvent );

		if ( !CanWriteChanges() )
			return;

		if ( e.Action == NotifyCollectionChangedAction.Add )
		{
			var change = new Change { Index = e.NewStartingIndex, Value = (T)e.NewItems[0], Type = e.Action };
			_changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Remove )
		{
			var change = new Change { Index = e.OldStartingIndex, Type = e.Action };
			_changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Reset )
		{
			AddResetChange();
		}
		else if ( e.Action == NotifyCollectionChangedAction.Replace )
		{
			var change = new Change { Index = e.OldStartingIndex, Type = e.Action, Value = (T)e.NewItems[0] };
			_changes.Add( change );
		}
		else if ( e.Action == NotifyCollectionChangedAction.Move )
		{
			var change = new Change { Index = e.OldStartingIndex, MovedIndex = e.NewStartingIndex, Type = e.Action };
			_changes.Add( change );
		}
	}

	private static T ReadValue( ref ByteStream data )
	{
		var value = Game.TypeLibrary.FromBytes<object>( ref data );
		return (T)value;
	}

	private static void WriteValue( T value, ref ByteStream data )
	{
		Game.TypeLibrary.ToBytes( value, ref data );
	}

	private bool CanWriteChanges() => !Parent?.IsProxy ?? true;

	private void AddResetChange()
	{
		var change = new Change { Type = NotifyCollectionChangedAction.Reset };
		_changes.Add( change );

		for ( var i = 0; i < _list.Count; i++ )
		{
			var item = _list[i];
			change = new Change { Index = -1, Value = item, Type = NotifyCollectionChangedAction.Add };
			_changes.Add( change );
		}
	}
}
