using System;
using System.IO;

namespace Rinten.Porting.SBox;

//Rotation that fixes fbx files whose declared axes differ from what the content was authored in
//Applied via the model definition transform so mesh, skeleton and clips stay in step
internal static class SBoxFbxAxes
{
	//How far into the file to look. Global settings sit near the front.
	private const int Header = 2 * 1024 * 1024;

	/// <summary>The turn about the upright, in degrees, that puts what a file declares as its front onto this engine's forward. Zero where the file says nothing, says something this cannot use, or already agrees.</summary>
	public static float Turn( string source )
	{
		if ( string.IsNullOrWhiteSpace( source ) ) return 0.0f;
		if ( !string.Equals( Path.GetExtension( source ), ".fbx", StringComparison.OrdinalIgnoreCase ) ) return 0.0f;

		var bytes = Head( source );
		if ( bytes is null ) return 0.0f;

		//Up has to be up: a file standing on a different axis is one whose whole basis differs, and half a turn about the vertical is not the answer to that.
		if ( Read( bytes, "UpAxis" ) is not 1 ) return 0.0f;
		if ( Read( bytes, "UpAxisSign" ) is not 1 ) return 0.0f;

		var front = Read( bytes, "FrontAxis" );
		var sign = Read( bytes, "FrontAxisSign" );

		if ( front is null || sign is null ) return 0.0f;

		//Where the file's front points, in the axes the file itself declares - which our importer keeps, so this is where it lands here too.
		var facing = front switch
		{
			0 => new Vector3( sign.Value, 0.0f, 0.0f ),
			2 => new Vector3( 0.0f, 0.0f, sign.Value ),
			_ => Vector3.Zero,
		};

		if ( facing == Vector3.Zero ) return 0.0f;

		//And the turn from there onto ours, about the upright. Both are axes, so it is a quarter turn at a time and there is nothing to round.
		var wanted = Vector3.Forward;

		if ( facing == wanted ) return 0.0f;
		if ( facing == -wanted ) return 180.0f;

		//A front along X: which way round is the sign of the cross product against the upright.
		return Vector3.Cross( facing, wanted ).y > 0.0f ? 90.0f : -90.0f;
	}

	/// <summary>What the file says, for a report that has to explain a turn.</summary>
	public static string Said( string source )
	{
		var bytes = Head( source );
		if ( bytes is null ) return "unreadable";

		return $"up={Read( bytes, "UpAxis" )}/{Read( bytes, "UpAxisSign" )} " +
			$"front={Read( bytes, "FrontAxis" )}/{Read( bytes, "FrontAxisSign" )} " +
			$"coord={Read( bytes, "CoordAxis" )}/{Read( bytes, "CoordAxisSign" )}";
	}

	private static byte[] Head( string source )
	{
		try
		{
			if ( !File.Exists( source ) ) return null;

			using var stream = File.OpenRead( source );

			var bytes = new byte[(int)Math.Min( stream.Length, Header )];
			var read = stream.Read( bytes, 0, bytes.Length );

			if ( read <= 0 ) return null;
			if ( read < bytes.Length ) Array.Resize( ref bytes, read );

			return bytes;
		}
		catch ( IOException )
		{
			return null;
		}
	}

	//Reads an axis field from binary fbx bytes: name, type strings, then an I record
	private static int? Read( byte[] bytes, string field )
	{
		var at = Find( bytes, field );
		if ( at < 0 ) return null;

		//Far enough to clear the name, the two type strings and the flags, and no further: the next field's record must not be read as this one's.
		var end = Math.Min( bytes.Length - 5, at + 220 );

		for ( var i = at + field.Length; i < end; i++ )
		{
			if ( bytes[i] != (byte)'I' ) continue;

			var value = BitConverter.ToInt32( bytes, i + 1 );

			//An axis is 0, 1 or 2 and its sign is 1 or -1; anything else is some other number that happened to follow an I.
			if ( value is >= -1 and <= 2 ) return value;
		}

		return null;
	}

	//Finds an exact property name; a real one is followed by S and a type-name length of 3
	private static int Find( byte[] bytes, string field )
	{
		for ( var i = 0; i + field.Length + 5 <= bytes.Length; i++ )
		{
			var found = true;

			for ( var c = 0; c < field.Length; c++ )
			{
				if ( bytes[i + c] == (byte)field[c] ) continue;

				found = false;
				break;
			}

			if ( !found ) continue;

			var at = i + field.Length;

			if ( bytes[at] != (byte)'S' ) continue;

			var length = BitConverter.ToUInt32( bytes, at + 1 );
			if ( length is 0 or > 64 ) continue;

			return i;
		}

		return -1;
	}
}
