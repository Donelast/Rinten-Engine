using System.Buffers.Binary;
using System.Text;

namespace Rinten.Resources;

//RERL (Resource External Reference List) support for ResourceWriter. Tracks external resource references and serializes them as a RERL block.
internal partial class ResourceWriter
{
	const uint RESOURCE_BLOCK_ID_RERL = 0x4C524552; //'RERL'

	readonly record struct ExternalRef( ulong Id, string Path );
	readonly List<ExternalRef> _externalRefs = [];

	/// <summary>Register an external resource reference (e.g., a material path). These are written as a RERL block so the resource system can resolve dependencies.</summary>
	public void AddExternalReference( string resourcePath )
	{
		if ( string.IsNullOrEmpty( resourcePath ) )
			return;

		var normalized = Resource.FixPath( resourcePath );
		var id = MurmurHash64( normalized );

		foreach ( var existing in _externalRefs )
		{
			if ( existing.Id == id )
				return;
		}

		_externalRefs.Add( new ExternalRef( id, normalized ) );
	}

	//Builds the RERL block: 8-byte header then 16-byte entries (id, name offset) and UTF-8 names
	byte[] BuildRERLBlock()
	{
		int count = _externalRefs.Count;

		const int headerSize = 8;   //CResourceArray: int32 offset + uint32 count
		const int entrySize = 16;   //ResourceReferenceInfo_t: uint64 + int32 + int32 pad
		int entriesStart = headerSize;
		int stringsStart = entriesStart + count * entrySize;

		var stringBytes = new byte[count][];
		int totalStringSize = 0;
		for ( int i = 0; i < count; i++ )
		{
			stringBytes[i] = Encoding.UTF8.GetBytes( _externalRefs[i].Path );
			totalStringSize += stringBytes[i].Length + 1; //+1 null terminator
		}

		var data = new byte[stringsStart + totalStringSize];

		//CResourceArray header
		BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( 0 ), entriesStart );
		BinaryPrimitives.WriteUInt32LittleEndian( data.AsSpan( 4 ), (uint)count );

		//Entries + strings
		int stringOffset = stringsStart;
		for ( int i = 0; i < count; i++ )
		{
			int entryPos = entriesStart + i * entrySize;
			int nameFieldPos = entryPos + 8; //position of m_pResourceName field

			BinaryPrimitives.WriteUInt64LittleEndian( data.AsSpan( entryPos ), _externalRefs[i].Id );
			BinaryPrimitives.WriteInt32LittleEndian( data.AsSpan( nameFieldPos ), stringOffset - nameFieldPos );
			//[entryPos + 12..15] = padding, already zero

			Array.Copy( stringBytes[i], 0, data, stringOffset, stringBytes[i].Length );
			//null terminator already zero from array init
			stringOffset += stringBytes[i].Length + 1;
		}

		return data;
	}

	//Resource id: MurmurHash64A of the normalised path, with the compiled format's seed
	static ulong MurmurHash64( string input )
	{
		const ulong m = 0xC6A4A7935BD1E995ul;
		const int r = 47;
		const ulong seed = 0xEDABCDEF;

		ReadOnlySpan<byte> key = Encoding.UTF8.GetBytes( input );

		var h = seed ^ ( (ulong)key.Length * m );

		var blocks = key.Length / 8;

		for ( var i = 0; i < blocks; ++i )
		{
			var k = BinaryPrimitives.ReadUInt64LittleEndian( key.Slice( i * 8, 8 ) );

			k *= m;
			k ^= k >> r;
			k *= m;

			h ^= k;
			h *= m;
		}

		var tail = key[( blocks * 8 )..];

		for ( var i = tail.Length - 1; i >= 0; --i ) h ^= (ulong)tail[i] << ( 8 * i );

		if ( tail.Length > 0 ) h *= m;

		h ^= h >> r;
		h *= m;
		h ^= h >> r;

		return h;
	}
}
