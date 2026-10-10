using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Rinten.Porting.SBox;

//Fbx UnitScaleFactor (cm per unit); s&box assumed inches, so the port reconciles the two
internal static class SBoxFbxUnits
{
	//What the format assumes when a file says nothing: a centimetre.
	private const float Assumed = 0.01f;

	//The name the value is stored under, in both spellings of the format.
	private const string Field = "UnitScaleFactor";

	//How far in the header is looked at. Global settings are written near the front.
	private const int Header = 2 * 1024 * 1024;

	/// <summary>One unit of this file in metres. A centimetre where the file does not say, which is what the format itself assumes.</summary>
	public static float Metres( string source )
	{
		if ( string.IsNullOrWhiteSpace( source ) || !File.Exists( source ) ) return Assumed;

		byte[] bytes;

		try
		{
			using var stream = File.OpenRead( source );

			bytes = new byte[(int)Math.Min( stream.Length, Header )];

			var read = stream.Read( bytes, 0, bytes.Length );

			if ( read < bytes.Length ) Array.Resize( ref bytes, Math.Max( read, 0 ) );
		}
		catch ( IOException )
		{
			return Assumed;
		}

		var found = Packed( bytes ) ?? Written( bytes );

		//A file claiming nothing, or nonsense, is a file we know nothing about rather than one to divide by.
		if ( found is not { } factor || factor <= 0.0f || float.IsNaN( factor ) ) return Assumed;

		//Their number is in centimetres; ours is a metre.
		return factor * 0.01f;
	}

	//Walks the property's length-prefixed strings to the value rather than searching for it
	private static float? Packed( byte[] bytes )
	{
		var at = Find( bytes, Encoding.ASCII.GetBytes( Field ) );

		if ( at < 0 ) return null;

		var i = at + Field.Length;
		var end = Math.Min( bytes.Length, i + 256 );

		while ( i < end )
		{
			switch ( (char)bytes[i] )
			{
				//A string: four bytes of length and then that many of text.
				case 'S':
				case 'R':
					if ( i + 5 > bytes.Length ) return null;

					var length = BitConverter.ToInt32( bytes, i + 1 );

					if ( length < 0 || length > 4096 ) return null;

					i += 5 + length;
					break;

				case 'D':
					return i + 9 <= bytes.Length ? (float)BitConverter.ToDouble( bytes, i + 1 ) : null;

				case 'F':
					return i + 5 <= bytes.Length ? BitConverter.ToSingle( bytes, i + 1 ) : null;

				case 'I':
					return i + 5 <= bytes.Length ? BitConverter.ToInt32( bytes, i + 1 ) : null;

				case 'L':
					return i + 9 <= bytes.Length ? BitConverter.ToInt64( bytes, i + 1 ) : null;

				default:
					i++;
					break;
			}
		}

		return null;
	}

	//The value out of a text fbx, where the whole property is one line and the value is the last thing on it.
	private static float? Written( byte[] bytes )
	{
		var text = Encoding.UTF8.GetString( bytes );

		var at = text.IndexOf( Field, StringComparison.Ordinal );

		if ( at < 0 ) return null;

		var stop = text.IndexOf( '\n', at );
		var line = stop < 0 ? text[at..] : text[at..stop];

		var comma = line.LastIndexOf( ',' );

		if ( comma < 0 ) return null;

		return float.TryParse( line[(comma + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value )
			? value
			: null;
	}

	private static int Find( byte[] haystack, byte[] needle )
	{
		for ( var i = 0; i + needle.Length <= haystack.Length; i++ )
		{
			var same = true;

			for ( var j = 0; j < needle.Length; j++ )
			{
				if ( haystack[i + j] == needle[j] ) continue;

				same = false;
				break;
			}

			if ( same ) return i;
		}

		return -1;
	}
}
