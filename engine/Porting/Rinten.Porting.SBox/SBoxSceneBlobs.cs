using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rinten.Porting.SBox;

//Inlines editor-built mesh blobs (base64 or _d sidecar) into the scene, converted to our units/axes
internal static class SBoxSceneBlobs
{
	//Version of one blob's payload this understands.
	private const int PayloadVersion = 1;

	//Version of a sidecar full of them.
	private const int FileVersion = 1;

	//A sidecar's header, and one entry of its table. Eight bytes of header - a version and a count
	//- and then, for each blob, a guid, the blob's own version, where it starts and how long it is.
	private const int HeaderSize = 8;
	private const int TocEntrySize = 32;

	//As many named sections as a payload is allowed to claim, and as long as one of their names is allowed to be. A string in
	//a scene is a payload only if it reads as one, and these are what stop a long ordinary string from being taken for a mesh.
	private const int MaxSections = 64;
	private const int MaxSectionName = 64;

	//The one section that holds lengths. The others are connectivity, colours, texture coordinates, material indices and edge flags - none of which is a
	//distance. Anything this does not recognise is skipped by its own length rather than guessed at, so a section added later passes through untouched.
	private const string Positions = "positions";

	//Only property whose reader accepts a base64 blob; elsewhere a string reads as null
	private const string MeshData = "Data";

	//s&box's axes, Source's, and the turn onto ours.
	private static ForeignBasis Axes => ForeignBasis.Quake;

	//What one scene's blobs did or did not come to.
	internal readonly record struct Carried( int Vertices, int Inlined, int Missing );

	/// <summary>Puts the scene's geometry into the scene, in our units and our axes.</summary>
	public static Carried Convert( string scene, string sidecar, float scale )
	{
		if ( string.IsNullOrWhiteSpace( scene ) || !File.Exists( scene ) ) return default;

		JsonNode root;

		try { root = JsonNode.Parse( File.ReadAllText( scene ) ); }
		catch ( Exception ) { return default; }

		if ( root is null ) return default;

		var blobs = Sidecar( sidecar );
		var carried = new Carried();

		Walk( root, blobs, scale, ref carried );

		if ( carried.Vertices == 0 && carried.Inlined == 0 ) return carried;

		try
		{
			File.WriteAllText( scene,
				root.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
		}
		catch ( IOException )
		{
			return default;
		}

		return carried;
	}

	//Every blob in a sidecar, by the guid the json names it with. A file this does not understand is an empty table rather than an error: what it
	//costs is a reference that cannot be resolved, and that is reported where it is found, one mesh at a time, rather than as a file nobody can place.
	private static Dictionary<Guid, byte[]> Sidecar( string path )
	{
		var found = new Dictionary<Guid, byte[]>();

		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) ) return found;

		byte[] bytes;

		try { bytes = File.ReadAllBytes( path ); }
		catch ( IOException ) { return found; }

		if ( bytes.Length < HeaderSize ) return found;
		if ( BitConverter.ToInt32( bytes, 0 ) != FileVersion ) return found;

		var count = BitConverter.ToInt32( bytes, 4 );

		if ( count <= 0 || bytes.Length < HeaderSize + ( (long)count * TocEntrySize ) ) return found;

		for ( var i = 0; i < count; i++ )
		{
			var entry = HeaderSize + ( i * TocEntrySize );

			var guid = new Guid( bytes.AsSpan( entry, 16 ) );

			//The blob's own version belongs to whatever reads it, not to the table: where it starts and how long it is are all this needs.
			var offset = BitConverter.ToInt64( bytes, entry + 20 );
			var size = BitConverter.ToInt32( bytes, entry + 28 );

			if ( offset < 0 || size < 0 || offset + size > bytes.Length ) return found;

			found[guid] = bytes.AsSpan( (int)offset, size ).ToArray();
		}

		return found;
	}

	//Every string and every reference under this node, rewritten where it turns out to be a mesh.
	private static void Walk( JsonNode node, Dictionary<Guid, byte[]> blobs, float scale, ref Carried carried )
	{
		switch ( node )
		{
			//Over a copy of the keys: a value is replaced while the walk is standing on it, and a document cannot be changed while it is being enumerated.
			case JsonObject holder:
				foreach ( var pair in holder.ToList() )
				{
					if ( Rewritten( pair.Value, scale, ref carried ) is { } text ) holder[pair.Key] = text;
					else if ( pair.Key == MeshData && Inlined( pair.Value, blobs, scale, ref carried ) is { } data )
						holder[pair.Key] = data;
					else if ( pair.Value is not null ) Walk( pair.Value, blobs, scale, ref carried );
				}

				break;

			case JsonArray list:
				for ( var i = 0; i < list.Count; i++ )
				{
					if ( Rewritten( list[i], scale, ref carried ) is { } text ) list[i] = text;
					else if ( list[i] is not null ) Walk( list[i], blobs, scale, ref carried );
				}

				break;
		}
	}

	//Mesh a $blob reference points at, inlined; null if unresolvable (counted missing)
	private static string Inlined( JsonNode node, Dictionary<Guid, byte[]> blobs, float scale, ref Carried carried )
	{
		if ( node is not JsonObject holder ) return null;
		if ( holder["$blob"]?.GetValue<string>() is not { } named ) return null;
		if ( !Guid.TryParse( named, out var guid ) ) return null;

		if ( !blobs.TryGetValue( guid, out var payload ) )
		{
			carried = carried with { Missing = carried.Missing + 1 };
			return null;
		}

		//On a copy of the blob, which is committed only once the whole payload has been read to its end - see Rewritten, for the same reason.
		var bytes = payload.ToArray();
		var moved = 0;

		if ( !ScalePayload( bytes, 0, bytes.Length, scale, ref moved ) )
		{
			carried = carried with { Missing = carried.Missing + 1 };
			return null;
		}

		carried = carried with { Vertices = carried.Vertices + moved, Inlined = carried.Inlined + 1 };

		return System.Convert.ToBase64String( bytes );
	}

	//Mesh payload rescaled, or null; re-encoded only if fully read, so no half-converted mesh
	private static string Rewritten( JsonNode node, float scale, ref Carried carried )
	{
		if ( node is not JsonValue value ) return null;
		if ( !value.TryGetValue<string>( out var text ) ) return null;

		//The smallest thing this could be is a header, a named section and a vertex; anything base64 is a multiple of four characters.
		if ( text is null || text.Length < 32 || text.Length % 4 != 0 ) return null;

		var bytes = new byte[( text.Length / 4 ) * 3];

		if ( !System.Convert.TryFromBase64String( text, bytes, out var written ) ) return null;

		var here = 0;

		if ( !ScalePayload( bytes, 0, written, scale, ref here ) || here == 0 ) return null;

		carried = carried with { Vertices = carried.Vertices + here };

		return System.Convert.ToBase64String( bytes, 0, written );
	}

	//One blob: a version, a count of sections, and then a name and a length for each. Self
	//describing on purpose, so a section this does not know is stepped over rather than misread.
	private static bool ScalePayload( byte[] bytes, int offset, int size, float scale, ref int moved )
	{
		var end = offset + size;
		var at = offset;

		if ( at + 8 > end ) return false;

		if ( BitConverter.ToInt32( bytes, at ) != PayloadVersion ) return false;
		at += 4;

		var sections = BitConverter.ToInt32( bytes, at );
		at += 4;

		if ( sections <= 0 || sections > MaxSections ) return false;

		for ( var i = 0; i < sections; i++ )
		{
			if ( at + 4 > end ) return false;

			var nameLength = BitConverter.ToInt32( bytes, at );
			at += 4;

			if ( nameLength <= 0 || nameLength > MaxSectionName || at + nameLength + 4 > end ) return false;

			var name = Encoding.UTF8.GetString( bytes, at, nameLength );
			at += nameLength;

			var length = BitConverter.ToInt32( bytes, at );
			at += 4;

			if ( length < 0 || at + length > end ) return false;

			//Three floats a vertex. A section that is not a whole number of those is not the one this is looking for, whatever it is called.
			if ( name == Positions && length % 12 == 0 )
			{
				//A vertex at a time rather than a float at a time: the axes move as well as the units, and a permutation cannot be done to one component in isolation.
				for ( var f = 0; f < length; f += 12 )
				{
					var theirs = new Vector3(
						BitConverter.ToSingle( bytes, at + f ),
						BitConverter.ToSingle( bytes, at + f + 4 ),
						BitConverter.ToSingle( bytes, at + f + 8 ) );

					var ours = Axes.Vector( theirs ) * scale;

					BitConverter.TryWriteBytes( bytes.AsSpan( at + f, 4 ), ours.x );
					BitConverter.TryWriteBytes( bytes.AsSpan( at + f + 4, 4 ), ours.y );
					BitConverter.TryWriteBytes( bytes.AsSpan( at + f + 8, 4 ), ours.z );
				}

				moved += length / 12;
			}

			at += length;
		}

		//Exactly at the end, not merely somewhere inside it. A string that decoded to something shaped like a payload but with bytes left over is not one.
		return at == end;
	}
}
