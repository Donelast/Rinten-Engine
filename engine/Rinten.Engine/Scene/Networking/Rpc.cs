using Rinten.Utility;

namespace Rinten;

/// <summary>RPC called on all clients including host, after synced state (except Sync.Query) is up to date</summary>
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Rinten.Rpc.OnCallRpc" )]
[Obsolete( "Please use Rpc.Broadcast" )]
public sealed class BroadcastAttribute : RpcAttribute
{
	public BroadcastAttribute() : base( RpcMode.Broadcast ) { }

	NetPermission _permission;

	public NetPermission Permission
	{
		get => _permission;
		set
		{
			_permission = value;

			if ( _permission == NetPermission.HostOnly ) Flags |= NetFlags.HostOnly;
			if ( _permission == NetPermission.OwnerOnly ) Flags |= NetFlags.OwnerOnly;
		}
	}

	public BroadcastAttribute( NetPermission permission ) : base( RpcMode.Broadcast )
	{
		Permission = permission;
	}
}

/// <summary>RPC sent to the object's owner (or host), after synced state (except Sync.Query) is up to date</summary>
[CodeGenerator( CodeGeneratorFlags.Instance | CodeGeneratorFlags.WrapMethod, "__rpc_Wrapper" )]
[CodeGenerator( CodeGeneratorFlags.Static | CodeGeneratorFlags.WrapMethod, "Rinten.Rpc.OnCallRpc" )]
[Obsolete( "Please use Rpc.Owner" )]
public sealed class AuthorityAttribute : RpcAttribute
{
	public AuthorityAttribute() : base( RpcMode.Owner ) { }

	NetPermission _permission;

	public NetPermission Permission
	{
		get => _permission;
		set
		{
			_permission = value;

			if ( _permission == NetPermission.HostOnly ) Flags |= NetFlags.HostOnly;
			if ( _permission == NetPermission.OwnerOnly ) Flags |= NetFlags.OwnerOnly;
		}
	}

	public AuthorityAttribute( NetPermission permission ) : base( RpcMode.Owner )
	{
		Permission = permission;
	}
}

/// <summary>Specifies who can invoke an action over the network.</summary>
[Expose, Obsolete]
public enum NetPermission
{
	/// <summary>Anyone can invoke this.</summary>
	Anyone,

	/// <summary>Only the host can invoke this.</summary>
	HostOnly,

	/// <summary>Only the owner can invoke this. If the action is static, this works the same way as <see cref="HostOnly"/>.</summary>
	OwnerOnly
}

public static partial class Rpc
{
	/// <summary>The <see cref="Connection"/> that is calling this method.</summary>
	public static Connection Caller { get; private set; }

	/// <summary>The id of the <see cref="Connection"/> that is calling this method.</summary>
	public static Guid CallerId => Caller.Id;

	/// <summary>Whether we're currently being called from a remote <see cref="Connection"/>.</summary>
	public static bool Calling { get; private set; }

	internal static Connection.Filter? Filter { get; private set; }

	internal static DisposeAction<Connection, bool> WithCaller( Connection caller )
	{
		var oldCaller = Caller;
		var oldCalling = Calling;

		Calling = true;
		Caller = caller;

		unsafe
		{
			return new DisposeAction<Connection, bool>( &RestoreCaller, oldCaller, oldCalling );
		}
	}

	static void RestoreCaller( Connection oldCaller, bool oldCalling )
	{
		Calling = oldCalling;
		Caller = oldCaller;
	}

	//Resume a method from an RPC. If the RPC caller is our local connection then we'll first disable any active filter and restore it afterwards.
	internal static void Resume( WrappedMethod m )
	{
		try
		{
			if ( Caller != Connection.Local )
			{
				m.Resume?.Invoke();
				return;
			}

			var oldFilter = Filter;
			Filter = null;

			try
			{
				m.Resume?.Invoke();
			}
			finally
			{
				Filter = oldFilter;
			}
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	/// <summary>Called right before calling an RPC function.</summary>
	public static void PreCall()
	{
		if ( Calling )
		{
			Calling = false;
			return;
		}

		Caller = Connection.Local;
	}

	/// <summary>Filter the recipients of any Rpc called in this scope to only include the specified <see cref="Connection"/> set.</summary>
	public static IDisposable FilterInclude( IEnumerable<Connection> connections )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Include, connections );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>Filter the recipients of any Rpc called in this scope to only include a <see cref="Connection"/> based on a predicate.</summary>
	public static IDisposable FilterInclude( Predicate<Connection> predicate )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Include, predicate );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>Filter the recipients of any Rpc called in this scope to only include the specified <see cref="Connection"/>.</summary>
	public static IDisposable FilterInclude( Connection connection )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Include, c => c == connection );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>Filter the recipients of any Rpc called in this scope to exclude a <see cref="Connection"/> based on a predicate.</summary>
	public static IDisposable FilterExclude( Predicate<Connection> predicate )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Exclude, predicate );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>Filter the recipients of any Rpc called in this scope to exclude the specified <see cref="Connection"/> set.</summary>
	public static IDisposable FilterExclude( IEnumerable<Connection> connections )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Exclude, connections );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}

	/// <summary>Filter the recipients of any Rpc called in this scope to exclude the specified <see cref="Connection"/>.</summary>
	public static IDisposable FilterExclude( Connection connection )
	{
		if ( Filter.HasValue )
			throw new InvalidOperationException( "An RPC filter is already active" );

		Filter = new( Connection.Filter.FilterType.Exclude, c => c == connection );

		return DisposeAction.Create( () =>
		{
			Filter = null;
		} );
	}
}
