namespace Rinten;

public partial class Component
{
	private Guid _id = Guid.NewGuid();
	public Guid Id
	{
		get => _id;
		private set
		{
			if ( _id == value ) return;

			var oldId = _id;
			_id = value;

			Scene?.Directory?.Add( this, oldId );
		}
	}

	//Should only be called by GameObjectDirectory.Add.
	internal void ForceChangeId( Guid guid )
	{
		_id = guid;
	}

	//Allows overriding the ID of this object. This should be used sparingly, and only when
	//necessary. This is generally used for network reasons, to make something deterministic.
	internal void SetDeterministicId( Guid id )
	{
		Id = id;
	}
}
