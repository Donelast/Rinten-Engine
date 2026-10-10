namespace Rinten;

//An interface for specifying how a custom type can be serialized and deserialized over the network with support for only sending changes.
internal interface INetworkSerializer
{
	//Write any changes to the ByteStream. This is only applicable if the type that implements this also implements INetworkReliable.
	void WriteChanged( ref ByteStream data );

	//Write all data to the ByteStream.
	void WriteAll( ref ByteStream data );

	//Read data from a ByteStream.
	void Read( ref ByteStream data );

	//Whether we currently have changes (are we dirty?)
	bool HasChanges { get; }
}
