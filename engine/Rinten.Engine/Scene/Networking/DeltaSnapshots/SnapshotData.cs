namespace Rinten.Network;

internal class SnapshotData : Dictionary<int, byte[]>, IObjectPoolEvent
{
	public static NetworkObjectPool<SnapshotData> Pool { get; } = new();

	private int ReferenceCount { get; set; }

	/// <summary>Add to reference count for this object.</summary>
	public void AddReference()
	{
		ReferenceCount++;
	}

	/// <summary>Release a reference for this object, and return it to the pool if nothing else is referencing it.</summary>
	public void Release()
	{
		if ( ReferenceCount == 0 )
			throw new InvalidOperationException( "ReferenceCount is already zero" );

		ReferenceCount--;

		if ( ReferenceCount <= 0 )
		{
			Pool.Return( this );
		}
	}

	void IObjectPoolEvent.OnRented()
	{
		ReferenceCount = 1;
	}

	void IObjectPoolEvent.OnReturned()
	{
		Clear();
	}
}
