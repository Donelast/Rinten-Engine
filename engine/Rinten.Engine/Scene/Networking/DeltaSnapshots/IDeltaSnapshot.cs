namespace Rinten.Network;

internal interface IDeltaSnapshot
{
	//Unique identifier for this object.
	Guid Id { get; }

	//Whether this object is a proxy (or we own it.)
	bool IsProxy { get; }

	//Current snapshot version for this object.
	ushort SnapshotVersion { get; }

	//Called when a snapshot is received by a Connection.
	bool OnSnapshot( Connection source, DeltaSnapshot snapshot );

	//Update the transmit state for the target connections. This method should return true if we should transmit to ANY of these connections.
	bool UpdateTransmitState( Connection[] targets );

	//Should this snapshot transmit to the target Connection?
	bool ShouldTransmit( Connection target );

	//Write delta snapshot data and return the DeltaSnapshot object.
	internal LocalSnapshotState WriteSnapshotState();

	//Called when another client has acknowledged a delta snapshot.
	void OnSnapshotAck( Connection source, DeltaSnapshot snapshot, RemoteSnapshotState state );

	//Try to send a network update or do nothing if no update is required. This is most likely called after WriteSnapshotState.
	internal void SendNetworkUpdate( bool queryValues = false );
}
