using Rinten.Internal;

namespace Rinten.Network;

/// <summary>An instance of this is created by the NetworkSystem when a server is joined, or created. You should not try to create this manually.</summary>
public abstract partial class GameNetworkSystem : IDisposable
{
	internal TypeLibrary Library { get; set; }
	internal NetworkSystem NetworkSystem { get; set; }

	public GameNetworkSystem()
	{
	}

	public virtual void Dispose()
	{

	}

	/// <summary>Called on the host to decide whether to accept a <see cref="Connection"/>.</summary>
	public virtual bool AcceptConnection( Connection channel, ref string reason )
	{
		return true;
	}

	public virtual void GetSnapshot( Connection source, ref SnapshotMsg msg ) { }

	public virtual Task SetSnapshotAsync( SnapshotMsg data ) => Task.CompletedTask;

	/// <summary>Called when the network system should handle initialization.</summary>
	public virtual void OnInitialize()
	{

	}

	/// <summary>A client has connected to the server but hasn't fully finished joining yet.</summary>
	public virtual void OnConnected( Connection client ) { }

	//Fully joined the server. Can be called when changing the map too. The game should usually create some object for the player to control here.
	public virtual void OnJoined( Connection client ) { } //TODO rename OnActive

	/// <summary>A client has disconnected from the server.</summary>
	public virtual void OnLeave( Connection client ) { }

	/// <summary>The host left the server and you are now in charge.</summary>
	public virtual void OnBecameHost( Connection previousHost ) { }

	/// <summary>The current host has been changed.</summary>
	public virtual void OnHostChanged( Connection previousHost, Connection newHost ) { }

	internal void BroadcastRaw( ByteStream msg, Connection.Filter? filter, NetFlags flags )
	{
		NetworkSystem.Broadcast( msg, Connection.ChannelState.Snapshot, filter, flags );
	}

	//Whether the host is busy right now. This can be used to determine if the host can be changed.
	internal virtual bool IsHostBusy => true;

	internal IEnumerable<Connection> GetFilteredConnections( Connection.ChannelState minimumState = Connection.ChannelState.Snapshot, Connection.Filter? filter = null )
	{
		return NetworkSystem.GetFilteredConnections( minimumState, filter );
	}

	public void BroadcastRaw( ByteStream msg, Connection.Filter? filter = null )
	{
		BroadcastRaw( msg, filter, NetFlags.Reliable );
	}

	internal void Broadcast<T>( T obj, Connection.Filter? filter, NetFlags flags )
	{
		var bs = ByteStream.Create( 512 );
		bs.Write( InternalMessageType.Packed );

		try
		{
			Library.ToBytes( obj, ref bs );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when trying to network serialize object: {e.Message}" );
		}

		BroadcastRaw( bs, filter, flags );
		bs.Dispose();
	}

	public void Broadcast<T>( T obj, Connection.Filter? filter = null )
	{
		Broadcast( obj, filter, NetFlags.Reliable );
	}

	internal void Send( Connection connection, InternalMessageType type, ReadOnlySpan<byte> data, NetFlags flags )
	{
		using var bs = ByteStream.Create( 512 );
		bs.Write( type );
		bs.Write( data.Length );
		bs.Write( data );

		//Do we really have a valid connection to this?
		var targetConnection = NetworkSystem.FindConnection( connection.Id );

		if ( targetConnection is null )
		{
			if ( NetworkSystem.Connection is null )
				return;

			var wrapper = new TargetedInternalMessage
			{
				SenderId = Connection.Local.Id,
				TargetId = connection.Id,
				Data = bs.ToArray(),
				Flags = (byte)flags
			};

			NetworkSystem.Connection.SendMessage( wrapper, flags );
		}
		else
		{
			targetConnection.SendStream( bs, flags );
		}
	}

	internal void Send( Connection connection, InternalMessageType type, byte[] data, NetFlags flags )
	{
		Send( connection, type, data.AsSpan(), flags );
	}

	internal void Send<T>( Connection connection, T obj, NetFlags flags )
	{
		var bs = ByteStream.Create( 512 );
		bs.Write( InternalMessageType.Packed );

		try
		{
			Library.ToBytes( obj, ref bs );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error when trying to network serialize object: {e.Message}" );
		}

		connection.SendStream( bs, flags );
		bs.Dispose();
	}

	internal void Send<T>( Guid connectionId, T obj, NetFlags flags )
	{
		var connection = NetworkSystem.FindConnection( connectionId );

		if ( connection is not null )
		{
			var bs = ByteStream.Create( 512 );
			bs.Write( InternalMessageType.Packed );

			try
			{
				Library.ToBytes( obj, ref bs );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Error when trying to network serialize object: {e.Message}" );
			}

			connection.SendStream( bs, flags );

			bs.Dispose();
		}
		else if ( NetworkSystem.Connection is not null )
		{
			var wrapper = new TargetedMessage
			{
				SenderId = Connection.Local.Id,
				TargetId = connectionId,
				Message = obj,
				Flags = (byte)flags
			};

			NetworkSystem.Connection.SendMessage( wrapper, flags );
		}
	}

	public void Send<T>( Guid connectionId, T obj )
	{
		Send( connectionId, obj, NetFlags.Reliable );
	}

	/// <summary>Allows to push some kind of scope when reading network messages. This is useful if you need to adjust Time.Now etc.</summary>
	public virtual IDisposable Push() => null;

	//The scene scope Push exists for, as the struct itself, so the network read each tick does not box a scope only to dispose of it.
	//Used only while Push is the engine's own - a game that overrides it gets its override. See NetworkSystem.HandleIncomingMessages.
	internal virtual ScenePushScope PushScene() => default;

	public void AddHandler<T>( Action<T, Connection, Guid> handler )
	{
		NetworkSystem.AddHandler<T>( handler );
	}

	public void AddHandler<T>( Func<T, Connection, Guid, Task> handler )
	{
		NetworkSystem.AddHandler<T>( handler );
	}

	public void AddHandler<T>( Action<T, Connection> handler )
	{
		AddHandler<T>( ( t, c, g ) => handler( t, c ) );
	}

	internal void TickInternal()
	{
		Tick();
	}

	//Called every frame
	protected virtual void Tick()
	{

	}

	//A heartbeat has been received from the host. We should make sure our times are in sync.
	internal virtual void OnHeartbeat( double serverGameTime )
	{

	}

	//We've received a cull state change for a networked object.
	internal virtual void OnCullStateChangeMessage( ByteStream data, Connection source )
	{

	}

	//A delta snapshot message has been received from another connection.
	internal virtual void OnDeltaSnapshotMessage( InternalMessageType type, ByteStream data, Connection source )
	{

	}
}
