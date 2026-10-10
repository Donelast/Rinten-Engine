using Rinten.Network;

namespace Rinten;

public abstract partial class Connection
{
	//Key-value store for connections. Not to be mistaken with Info which is sent from the client
	//and can be used for things like avatar customization. This is not shared to other connections.
	internal Dictionary<string, object> KeyValues { get; } = new();

	//Get a typed value from the connection's key-value store.
	internal T Get<T>( string key, T defaultValue = default )
	{
		if ( KeyValues.TryGetValue( key, out var val ) && val is T typed )
			return typed;

		return defaultValue;
	}

	//Set a typed value in the connection's key-value store.
	internal void Set<T>( string key, T value )
	{
		KeyValues[key] = value;
	}
}
