using System.IO;
using System.Runtime.InteropServices;

namespace Rinten;

public static partial class RintenSystemExtensions
{
	/// <summary>Read a null terminated string from the stream, at given offset.</summary>
	public static string ReadNullTerminatedString( this Stream stream, long offset )
	{
		stream.Seek( offset, SeekOrigin.Begin );

		int b;
		List<byte> bytes = new List<byte>();
		while ( (b = stream.ReadByte()) != 0x00 )
		{
			bytes.Add( (byte)b );
		}

		return Encoding.UTF8.GetString( bytes.ToArray() );
	}
}
