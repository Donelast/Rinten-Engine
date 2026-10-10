namespace Rinten.Network;

//Can be implemented on a type so that when used in conjunction with [Sync] you can write and read directly from delta snapshots.
internal interface INetworkDeltaSnapshot
{
	//Write to a DeltaSnapshot.
	void WriteSnapshotState( int slot, LocalSnapshotState snapshot );

	//Read from a DeltaSnapshot.
	void ReadSnapshot( int slot, DeltaSnapshot snapshot );
}
