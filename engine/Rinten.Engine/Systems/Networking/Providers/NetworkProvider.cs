namespace Rinten.Network;

/// <summary>Pluggable transport (platform lobbies, relays) loaded from net/; only carries messages</summary>
public abstract class NetworkProvider
{
	/// <summary>The word before <c>://</c> in a connect target that names this provider - <c>steam</c> in <c>steam://lobby/123</c>.</summary>
	public abstract string Scheme { get; }

	/// <summary>What to call it where people read it.</summary>
	public abstract string Title { get; }

	/// <summary>Whether it can be used right now. A platform that is not running, or a library that is not there, makes the provider unavailable rather than missing - nothing is hosted or joined through it, and the rest of the game does not notice.</summary>
	public virtual bool IsAvailable => true;

	/// <summary>Whether a lobby is also opened through this provider whenever one is created.</summary>
	public virtual bool HostsByDefault => true;

	/// <summary>Who the platform says the local player is. Null to keep the id the engine made up.</summary>
	public virtual SteamId? LocalId => null;

	/// <summary>The local player's name on the platform. Null to keep the engine's.</summary>
	public virtual string LocalName => null;

	/// <summary>Start accepting players for a lobby.</summary>
	public abstract NetworkListener Listen( LobbyConfig config );

	/// <summary>Start connecting to what follows <c>scheme://</c> in a target.</summary>
	public abstract NetworkPeer Connect( string target );

	/// <summary>Lobbies this provider can see. Empty where it has no list.</summary>
	public virtual Task<List<LobbyInformation>> QueryLobbies( IReadOnlyDictionary<string, string> filters )
		=> Task.FromResult( new List<LobbyInformation>() );

	//What the running game is called, so lobbies of different games on one shared
	//platform id - Steam's Spacewar is everybody's test id - do not list each other.
	protected static string GameIdent => Application.GamePackage?.GetIdent( false, true ) ?? "rinten";

	/// <summary>Tell the platform what the player is doing, for its friends list - a status line, a map, a party size. See <see cref="Networking.SetRichPresence"/>. Null removes the key.</summary>
	public virtual void SetRichPresence( string key, string value ) { }

	/// <summary>Take back everything <see cref="SetRichPresence"/> said.</summary>
	public virtual void ClearRichPresence() { }

	/// <summary>Every frame, on the main thread.</summary>
	public virtual void Tick() { }

	/// <summary>The game is closing.</summary>
	public virtual void Shutdown() { }

	//Something outside the game asked to join - a friend's invite, a "join game"
	//from a platform's friends list. The target is what Networking.Connect takes.
	protected void RequestJoin( string target ) => NetworkProviders.RequestJoin( this, target );
}

/// <summary>One other player, through a provider.</summary>
public abstract class NetworkPeer
{
	/// <summary>Something to show for where they are.</summary>
	public abstract string Address { get; }

	/// <summary>Seconds, as the wire measures them.</summary>
	public virtual float Latency => 0;

	public virtual ConnectionStats Stats => default;

	/// <summary>False once they have gone, by their choice or the network's.</summary>
	public abstract bool IsConnected { get; }

	/// <summary>Why they went, once <see cref="IsConnected"/> is false.</summary>
	public virtual string EndReason => null;

	/// <summary>Who the platform says this is, if it says.</summary>
	public virtual SteamId? RemoteId => null;

	/// <summary>Send a message. Safe to call from any thread.</summary>
	public abstract void Send( byte[] data, bool reliable );

	/// <summary>The next message that has arrived, if any.</summary>
	public abstract bool TryReceive( out byte[] data );

	public abstract void Close( int code, string reason );
}

/// <summary>What a provider listens with for as long as a lobby is open.</summary>
public abstract class NetworkListener : IDisposable
{
	/// <summary>A player who has arrived since last asked.</summary>
	public abstract bool TryAccept( out NetworkPeer peer );

	public virtual void SetData( string key, string value ) { }
	public virtual void SetServerName( string name ) { }
	public virtual void SetMapName( string name ) { }

	/// <summary>Whatever the lobby's own data should say has changed - how many are in, who is the host.</summary>
	public virtual void Tick() { }

	public abstract void Dispose();
}

/// <summary>A lobby found through a provider.</summary>
public struct LobbyInformation
{
	/// <summary>The provider's <see cref="NetworkProvider.Scheme"/>.</summary>
	public string Provider { get; set; }

	/// <summary>What to hand to <see cref="Networking.Connect"/> to join it.</summary>
	public string Target { get; set; }

	public string Name { get; set; }
	public string Map { get; set; }
	public int Members { get; set; }
	public int MaxMembers { get; set; }

	/// <summary>What the host put there with <see cref="Networking.SetData"/>.</summary>
	public Dictionary<string, string> Data { get; set; }
}
