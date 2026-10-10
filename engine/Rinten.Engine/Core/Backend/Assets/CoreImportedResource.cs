using System.IO;

namespace Rinten;

//Reader for the legacy compiled resource container: header, block list and blocks
internal sealed class CoreImportedResource
{
	//Where a block is in the file, and how long it runs.
	internal readonly record struct Block( string Name, int Offset, int Size );

	/// <summary>The whole file.</summary>
	public byte[] Bytes { get; private init; }

	/// <summary>Its blocks, in the order they were written.</summary>
	public IReadOnlyList<Block> Blocks { get; private init; }

	/// <summary>Which version of the format the file claims.</summary>
	public int Version { get; private init; }

	private CoreImportedResource() { }

	/// <summary>Reads the envelope, or null if this is not one of their files.</summary>
	public static CoreImportedResource Open( byte[] bytes )
	{
		//A header is twenty bytes, and a file with nothing in it is not one.
		if ( bytes is null || bytes.Length < 24 ) return null;

		var fileSize = BitConverter.ToUInt32( bytes, 0 );
		var headerVersion = BitConverter.ToUInt16( bytes, 4 );
		var resourceVersion = BitConverter.ToUInt16( bytes, 6 );

		//Every compiled file they ship says twelve here. Anything else is a file that only looks like one.
		if ( headerVersion != 12 ) return null;

		if ( fileSize > bytes.Length ) return null;

		var blockOffset = BitConverter.ToUInt32( bytes, 8 );
		var blockCount = BitConverter.ToUInt32( bytes, 12 );

		if ( blockCount > 64 ) return null;

		var at = 8 + (int)blockOffset;
		var blocks = new List<Block>( (int)blockCount );

		for ( var i = 0; i < blockCount; i++ )
		{
			if ( at + 12 > bytes.Length ) return null;

			var name = System.Text.Encoding.ASCII.GetString( bytes, at, 4 );

			//Both offsets in this format are from the field that holds them, not from the start of the file.
			var offset = at + 4 + (int)BitConverter.ToUInt32( bytes, at + 4 );
			var size = (int)BitConverter.ToUInt32( bytes, at + 8 );

			if ( offset < 0 || size < 0 || offset + size > bytes.Length ) return null;

			blocks.Add( new Block( name, offset, size ) );

			at += 12;
		}

		return new CoreImportedResource
		{
			Bytes = bytes,
			Blocks = blocks,
			Version = resourceVersion,
		};
	}

	/// <summary>Reads the file at this path, or null if it is not one of theirs.</summary>
	public static CoreImportedResource Open( string path )
	{
		try
		{
			return File.Exists( path ) ? Open( SealedFile.ReadAllBytes( path ) ) : null;
		}
		catch ( IOException )
		{
			return null;
		}
	}

	/// <summary>What is in a block.</summary>
	public ReadOnlySpan<byte> Read( Block block ) => Bytes.AsSpan( block.Offset, block.Size );

	/// <summary>Every block of a name, in order. A model has one of some blocks and a run of others - a pair per level of detail.</summary>
	public IEnumerable<Block> All( string name )
	{
		foreach ( var block in Blocks )
		{
			if ( block.Name == name ) yield return block;
		}
	}

	/// <summary>The files this one names, in the order it names them.</summary>
	public IReadOnlyList<string> References()
	{
		var found = new List<string>();

		if ( Find( "RERL" ) is not { } block ) return found;

		var at = block.Offset;

		if ( at + 8 > Bytes.Length ) return found;

		//Both numbers here are relative to where they are written, as they are everywhere else in this format.
		var start = at + (int)BitConverter.ToUInt32( Bytes, at );
		var count = (int)BitConverter.ToUInt32( Bytes, at + 4 );

		if ( count < 0 || count > 4096 ) return found;

		for ( var i = 0; i < count; i++ )
		{
			//Eight bytes of identifier, then where the name is.
			var entry = start + ( i * 16 ) + 8;

			if ( entry + 8 > Bytes.Length ) break;

			var name = entry + (int)BitConverter.ToUInt32( Bytes, entry );

			if ( name < 0 || name >= Bytes.Length ) continue;

			var end = name;
			while ( end < Bytes.Length && Bytes[end] != 0 ) end++;

			if ( end > name ) found.Add( System.Text.Encoding.UTF8.GetString( Bytes, name, end - name ) );
		}

		return found;
	}

	/// <summary>The named block, or null where the file has none.</summary>
	public Block? Find( string name )
	{
		foreach ( var block in Blocks )
		{
			if ( block.Name == name ) return block;
		}

		return null;
	}
}
