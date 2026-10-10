namespace Rinten;

internal partial class BytePack
{
	/// <summary>Allows classes to specify how they are to be serialized and deserialized through BytePack.</summary>
	public interface ISerializer
	{
		/// <summary>Read from a <see cref="ByteStream"/> and return an object.</summary>
		static abstract object BytePackRead( ref ByteStream bs, Type targetType );

		/// <summary>Write a value to an outgoing <see cref="ByteStream"/>.</summary>
		static abstract void BytePackWrite( object value, ref ByteStream bs );
	}
}
