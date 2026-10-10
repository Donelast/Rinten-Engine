using Mono.Cecil;
using Rinten.Engine;
using Rinten.Internal;

namespace Rinten.Network;

internal partial class NetworkSystem
{
	internal TypeLibrary TypeLibrary { get; private set; }

	internal void Serialize<T>( T data, ref ByteStream bs )
	{
		TypeLibrary.ToBytes( data, ref bs );
	}

	internal T Deserialize<T>( ReadOnlySpan<byte> data )
	{
		return TypeLibrary.FromBytes<T>( data );
	}
}
