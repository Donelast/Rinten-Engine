using System.Reflection;
using System.Text.Json;

namespace Rinten;

//Registers a web build's precompiled game types in the library; a page cannot load code at runtime
internal static class WebCode
{
	private static readonly Logger log = new( "Web" );

	//What code this page was compiled from, by assembly: the fingerprints of its sources (see Fingerprint) as the web export
	//wrote them - one as an editor hosting the game sends them, one as a dedicated server does, without its server code.
	private static Dictionary<string, List<string>> compiledFrom = new( StringComparer.OrdinalIgnoreCase );

	//The fingerprints the export wrote - see WebManifest.Code.
	internal static void CompiledFrom( Dictionary<string, List<string>> code )
		=> compiledFrom = code is null ? new( StringComparer.OrdinalIgnoreCase ) : new( code, StringComparer.OrdinalIgnoreCase );

	//Whether the code a server sends is the code this page was compiled from. A page has no compiler, so a server's code can
	//only be taken where it already is the page's: the same sources, archive for archive. Null where it is; otherwise why not.
	internal static string Refuses( IEnumerable<(string Name, byte[] Archive)> sent )
	{
		foreach ( var (name, archive) in sent )
		{
			if ( !compiledFrom.TryGetValue( name, out var known ) )
				return $"This server runs code ({name}) that this web build was not made with.";

			string mine;

			try
			{
				mine = Fingerprint( archive );
			}
			catch ( Exception e )
			{
				return $"This server sent code ({name}) this web build cannot read: {e.Message}";
			}

			if ( !known.Contains( mine, StringComparer.OrdinalIgnoreCase ) )
				return $"This server runs a different version of {name} than this web build was made from - export the web build again from the server's project.";
		}

		return null;
	}

	//SHA-256 over a code archive's sources (path + text, path order), parsed without the compiler
	internal static string Fingerprint( byte[] archive )
	{
		using var compressed = ByteStream.CreateReader( archive );
		using var stream = compressed.Decompress();

		if ( stream.Read<string>() != "GMCA" ) throw new System.IO.InvalidDataException( "not a code archive" );

		_ = stream.Read<long>();
		_ = stream.Read<string>(); //the compiler's name
		_ = stream.Read<string>(); //its configuration
		_ = stream.Read<string>(); //the references

		var files = new SortedDictionary<string, string>( StringComparer.Ordinal );

		foreach ( var list in new[] { stream.Read<string>(), stream.Read<string>() } )
		{
			using var document = JsonDocument.Parse( list );

			foreach ( var file in document.RootElement.EnumerateArray() )
			{
				var path = file.GetProperty( "LocalPath" ).GetString() ?? "";
				files[path.Replace( '\\', '/' )] = file.GetProperty( "Text" ).GetString() ?? "";
			}
		}

		using var hash = System.Security.Cryptography.IncrementalHash.CreateHash( System.Security.Cryptography.HashAlgorithmName.SHA256 );

		foreach ( var (path, text) in files )
		{
			hash.AppendData( System.Text.Encoding.UTF8.GetBytes( path ) );
			hash.AppendData( [0] );
			hash.AppendData( System.Text.Encoding.UTF8.GetBytes( text.Replace( "\r\n", "\n" ) ) );
			hash.AppendData( [0] );
		}

		return Convert.ToHexString( hash.GetHashAndReset() );
	}

	//One of the game's assemblies, compiled into the module with the engine, into the library. Throws where the module has no such assembly.
	internal static Assembly Baked( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return null;

		var assembly = Assembly.Load( new AssemblyName( name ) );

		Game.TypeLibrary.AddAssembly( assembly, true );

		log.Info( $"compiled in: {assembly.GetName().Name}" );

		return assembly;
	}
}
