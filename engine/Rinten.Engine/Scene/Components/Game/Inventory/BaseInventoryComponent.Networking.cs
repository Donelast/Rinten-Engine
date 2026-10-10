namespace Rinten;

public partial class BaseInventoryComponent
{
	//Owner-only RPCs: clients request changes to their own inventory and the host applies them

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostRemove( BaseInventoryItem item ) => Remove( item );

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostDrop( BaseInventoryItem item ) => Drop( item );

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void HostMoveSlot( int fromSlot, int toSlot ) => MoveSlot( fromSlot, toSlot );
}
