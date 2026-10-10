namespace Rinten;

//An interface to implement to determine whether this object is a proxy.
internal interface INetworkProxy
{
	public bool IsProxy { get; }
}
