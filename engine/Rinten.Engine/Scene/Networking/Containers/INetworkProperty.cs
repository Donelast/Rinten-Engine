namespace Rinten;

//An interface for networked properties via SyncAttribute.
internal interface INetworkProperty
{
	//Called when initializing with a network table at the specified slot.
	void Init( int slot, INetworkProxy parent );
}
