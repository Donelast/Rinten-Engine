using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LayoutTests.Harness;

//Known failures are pinned to a hash of their mismatch; passing or failing differently fails the run
internal static class KnownFailures
{
	public static string DataDirectory => Path.Combine( AppContext.BaseDirectory, "Data", "Layout" );

	//Set to a file path to append every failing case's line there instead of failing.
	public const string RecordVariable = "RINTEN_LAYOUT_RECORD_KNOWN_FAILURES";

	private static readonly Lazy<Dictionary<string, string>> browser = new( () => Read( Path.Combine( DataDirectory, "known-failures.txt" ) ) );

	//Case name to signature, from Data/Layout/known-failures.txt .
	public static IReadOnlyDictionary<string, string> Browser => browser.Value;

	private static readonly object appendLock = new();

	//Reads name = SIGNATURE lines; blank lines and # comments are skipped.
	public static Dictionary<string, string> Read( string path )
	{
		var entries = new Dictionary<string, string>( StringComparer.Ordinal );
		if ( !File.Exists( path ) ) return entries;

		var lineNumber = 0;
		foreach ( var raw in File.ReadLines( path ) )
		{
			lineNumber++;
			var line = raw.Trim();
			if ( line.Length == 0 || line[0] == '#' ) continue;

			var separator = line.IndexOf( '=' );
			var name = separator > 0 ? line[..separator].Trim() : "";
			var signature = separator > 0 ? line[(separator + 1)..].Trim() : "";
			if ( name.Length == 0 || signature.Length != 64 || !signature.All( Uri.IsHexDigit ) )
				throw new InvalidDataException( $"{path}:{lineNumber}: expected 'case = SIGNATURE' with a 64-digit hex signature." );

			if ( !entries.TryAdd( name, signature.ToUpperInvariant() ) )
				throw new InvalidDataException( $"{path}:{lineNumber}: '{name}' is listed twice." );
		}

		return entries;
	}

	//SHA-256 over the report lines, one per line, as uppercase hex.
	public static string Signature( IReadOnlyList<string> report )
	{
		var builder = new StringBuilder();
		foreach ( var line in report ) builder.Append( line ).Append( '\n' );
		return Convert.ToHexString( SHA256.HashData( Encoding.UTF8.GetBytes( builder.ToString() ) ) );
	}

	//Decides one case. Returns null when the outcome is acceptable, otherwise the
	//message to fail with. Kept free of the assertion framework so any runner can use it.
	public static string Judge( IReadOnlyDictionary<string, string> known, string name, IReadOnlyList<string> report )
	{
		var signed = known.TryGetValue( name, out var expected );

		if ( report.Count == 0 )
			return signed ? $"{name} passes now. Remove it from the known-failure list." : null;

		var actual = Signature( report );

		var recordTo = Environment.GetEnvironmentVariable( RecordVariable );
		if ( !string.IsNullOrEmpty( recordTo ) )
		{
			lock ( appendLock ) File.AppendAllText( recordTo, $"{name} = {actual}\n" );
			return null;
		}

		if ( !signed )
			return $"{name}: {report.Count} mismatch(es) and not signed.\nSign with: {name} = {actual}\n{string.Join( "\n", report )}";

		if ( !string.Equals( actual, expected, StringComparison.OrdinalIgnoreCase ) )
			return $"{name}: fails differently from its signature. Review, then re-sign.\nSigned {expected}\nActual {actual}\n{string.Join( "\n", report )}";

		return null;
	}
}
