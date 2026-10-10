namespace Rinten;

//Extracts our own file from a legacy compiled resource container by its magic
internal static class CoreResource
{
	/// <summary>The block of <paramref name="bytes" /> that begins with <paramref name="magic" />, or null if there is none.</summary>
	public static byte[] Unwrap( byte[] bytes, uint magic )
	{
		//fileSize, headerVersion, resourceVersion, blockOffset, blockCount.
		if ( bytes is null || bytes.Length < 16 ) return null;

		var fileSize = System.BitConverter.ToUInt32( bytes, 0 );
		var blockOffset = System.BitConverter.ToUInt32( bytes, 8 );
		var blockCount = System.BitConverter.ToUInt32( bytes, 12 );

		//The offset is from where it is written, not from the start of the file.
		var at = 8L + blockOffset;

		if ( fileSize > bytes.Length || blockCount == 0 || blockCount > 64 ) return null;
		if ( at + ( blockCount * 12 ) > bytes.Length ) return null;

		for ( var i = 0; i < blockCount; i++ )
		{
			var entry = at + ( i * 12 );

			var offset = System.BitConverter.ToUInt32( bytes, (int)entry + 4 );
			var size = System.BitConverter.ToUInt32( bytes, (int)entry + 8 );

			//Same again: relative to the field it was read from.
			var start = entry + 4 + offset;

			if ( size < 4 || start + size > bytes.Length ) continue;
			if ( System.BitConverter.ToUInt32( bytes, (int)start ) != magic ) continue;

			var block = new byte[size];
			System.Array.Copy( bytes, start, block, 0, size );

			return block;
		}

		return null;
	}
}
