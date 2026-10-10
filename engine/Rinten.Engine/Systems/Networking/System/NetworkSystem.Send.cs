namespace Rinten.Network;

internal partial class NetworkSystem
{
	/// <summary>Send a message to all connections. You can optionally pass in a filter to determine who actually receives the message.</summary>
	public void Broadcast( ByteStream msg, Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null, NetFlags flags = NetFlags.Reliable )
	{
		IEnumerable<Connection> availableConnections = Networking.IsHost ? _connections : Connection.All;

		if ( Connection is not null )
		{
			availableConnections = availableConnections
				.Append( Connection )
				.Distinct();
		}

		//Encode once so every recipient gets the same wire bytes without re-compressing per connection.
		var encoded = Connection.Encode( msg );

		foreach ( var c in availableConnections )
		{
			if ( c == Connection.Local ) continue;
			if ( c.State < minimumState ) continue;
			if ( filter.HasValue && !filter.Value.IsRecipient( c ) ) continue;
			c.Send( encoded, flags );
		}
	}

	//Broadcast a packed message to all connections.
	internal void Broadcast<T>( T msg, Connection.ChannelState minimumState = Connection.ChannelState.Snapshot,
		Connection.Filter? filter = null, NetFlags flags = NetFlags.Reliable )
	{
		var bs = ByteStream.Create( 32 );

		bs.Write( InternalMessageType.Packed );
		TypeLibrary.ToBytes( msg, ref bs );

		Broadcast( bs, minimumState, filter, flags );

		bs.Dispose();
	}

	/// <summary>Get a list of connections that meet a specific criteria.</summary>
	public IEnumerable<Connection> GetFilteredConnections( Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null )
	{
		IEnumerable<Connection> availableConnections = Networking.IsHost ? _connections : Connection.All;

		if ( Connection is not null )
		{
			availableConnections = availableConnections
				.Append( Connection )
				.Distinct();
		}

		foreach ( var c in availableConnections )
		{
			if ( c == Connection.Local )
				continue;

			if ( c.State < minimumState )
				continue;

			if ( filter.HasValue && !filter.Value.IsRecipient( c ) )
				continue;

			yield return c;
		}
	}
}
