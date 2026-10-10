using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Rinten.Profiling;

//Timing comes from a scene's perf report (NAME.txt), cleanliness from its validated check run (NAME.check.txt)
internal static class Program
{
	const string Usage = """
		usage: benchreport report DIR             one run: a row per scene
		       benchreport compare DIR_A DIR_B    two runs side by side, check pictures compared
		       benchreport ab ROOT A B            bench.sh ab's rounds, averaged per side
		""";

	static readonly string[] Zones = { "cull", "cull cells", "cull instances", "cull clusters", "depth", "materials", "sky", "post pass", "compose" };

	static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

	private static int Main( string[] args )
	{
		if ( args.Length >= 2 && args[0] == "report" )
			return Report( args[1] ) > 0 ? 1 : 0;

		if ( args.Length >= 3 && args[0] == "compare" )
		{
			Compare( args[1], args[2] );
			return 0;
		}

		if ( args.Length >= 4 && args[0] == "ab" )
			return Ab( args[1], args[2], args[3] ) > 0 ? 1 : 0;

		Console.Error.WriteLine( Usage );
		return 2;
	}

	sealed class Row
	{
		public Dictionary<string, double> Values = new();
		public (bool On, int Errors, int Warnings)? Validation;

		public double? Get( string key ) => Values.TryGetValue( key, out var v ) ? v : null;
	}

	static string Read( string path ) => File.Exists( path ) ? File.ReadAllText( path ) : null;

	static double Number( string text ) => double.Parse( text, Inv );

	static Row Parse( string path )
	{
		var text = Read( path );
		if ( text is null )
			return null;

		var row = new Row();

		var m = Regex.Match( text.Split( '\n', 2 )[0], @"(\d+) fps" );
		if ( m.Success ) row.Values["fps"] = Number( m.Groups[1].Value );

		m = Regex.Match( text, @"frame typical ([\d.]+) ms" );
		if ( m.Success ) row.Values["typical"] = Number( m.Groups[1].Value );

		m = Regex.Match( text, @"gpu frame ([\d.]+) ms" );
		if ( m.Success ) row.Values["gpu"] = Number( m.Groups[1].Value );

		m = Regex.Match( text, @"validation (on|off), (\d+) errors, (\d+) warnings" );
		if ( m.Success ) row.Validation = (m.Groups[1].Value == "on", int.Parse( m.Groups[2].Value ), int.Parse( m.Groups[3].Value ));

		//First line of each named zone in the renderer's last frame
		var section = text.Split( "the renderer's last frame", 2 );
		if ( section.Length == 2 )
		{
			foreach ( var line in section[1].Split( '\n' ) )
			{
				m = Regex.Match( line, @"^\s+(\S.*?)\s+gpu\s+([\d.]+) ms" );
				var zone = m.Groups[1].Value;
				if ( m.Success && Zones.Contains( zone ) && !row.Values.ContainsKey( zone ) )
					row.Values[zone] = Number( m.Groups[2].Value );
			}
		}

		return row;
	}

	static List<string> ScenesIn( string folder ) => Directory.GetFiles( folder, "*.txt" )
		.Select( Path.GetFileName )
		.Where( f => !f.EndsWith( ".check.txt" ) && f != "summary.txt" )
		.Select( f => f[..^4] )
		.OrderBy( f => f, StringComparer.Ordinal )
		.ToList();

	static string Fmt( double? value, int width = 7, int digits = 2 )
		=> (value is { } v ? v.ToString( "F" + digits, Inv ) : "-").PadLeft( width );

	//"ok", "FAIL n errors", "no check" or "check died", and the warning count
	static (string State, int? Warnings) CheckOf( string folder, string name )
	{
		var row = Parse( Path.Combine( folder, name + ".check.txt" ) );
		if ( row?.Validation is not { } v )
		{
			var run = Read( Path.Combine( folder, name + ".check.run" ) ) ?? "";
			return (run.Length == 0 ? "no check" : "check died", null);
		}

		if ( !v.On )
			return ("not validated", v.Warnings);

		return (v.Errors == 0 ? "ok" : $"FAIL {v.Errors} errors", v.Warnings);
	}

	static bool Failed( string state ) => state.StartsWith( "FAIL" ) || state == "check died";

	static int Report( string folder )
	{
		var header = $"{"scene",-16} {"fps",7} {"typ ms",7} {"gpu ms",7} " + string.Join( " ", Zones.Select( z => z[..Math.Min( 7, z.Length )].PadLeft( 7 ) ) ) + "  validation";
		Console.WriteLine( header );
		Console.WriteLine( new string( '-', header.Length ) );

		var failed = 0;
		foreach ( var name in ScenesIn( folder ) )
		{
			var row = Parse( Path.Combine( folder, name + ".txt" ) );
			if ( row is null )
				continue;

			var (state, warnings) = CheckOf( folder, name );
			if ( Failed( state ) )
				failed++;

			var extra = warnings is > 0 ? $" ({warnings} warnings)" : "";
			Console.WriteLine( $"{name,-16} {Fmt( row.Get( "fps" ), 7, 0 )} {Fmt( row.Get( "typical" ) )} {Fmt( row.Get( "gpu" ) )} "
				+ string.Join( " ", Zones.Select( z => Fmt( row.Get( z ), 7, 3 ) ) ) + $"  {state}{extra}" );

			var other = Regex.Match( Read( Path.Combine( folder, name + ".run" ) ) ?? "", @"other Rinten processes (\d+)" );
			if ( other.Success && int.Parse( other.Groups[1].Value ) > 0 )
				Console.WriteLine( $"{"",-16} ({other.Groups[1].Value} other Rinten processes were running - noisier)" );
		}

		if ( failed > 0 )
			Console.WriteLine( $"\n{failed} scene(s) not clean under validation - read NAME.check.log" );

		return failed;
	}

	//Binary P6 as written by -perfwindowshot; null if it isn't one
	static (int W, int H, byte[] Rgb)? ReadPpm( string path )
	{
		if ( !File.Exists( path ) )
			return null;

		var data = File.ReadAllBytes( path );
		var pos = 0;

		string Token()
		{
			while ( pos < data.Length )
			{
				if ( data[pos] == '#' ) while ( pos < data.Length && data[pos] != '\n' ) pos++;
				else if ( char.IsWhiteSpace( (char)data[pos] ) ) pos++;
				else break;
			}

			var start = pos;
			while ( pos < data.Length && !char.IsWhiteSpace( (char)data[pos] ) ) pos++;
			return Encoding.ASCII.GetString( data, start, pos - start );
		}

		if ( Token() != "P6" || !int.TryParse( Token(), out var w ) || !int.TryParse( Token(), out var h ) || Token() != "255" )
			return null;

		pos++;
		if ( data.Length - pos < w * h * 3 )
			return null;

		return (w, h, data.AsSpan( pos, w * h * 3 ).ToArray());
	}

	static string Pixels( string a, string b )
	{
		if ( !File.Exists( a ) || !File.Exists( b ) )
			return "no pictures to compare";

		if ( ReadPpm( a ) is not { } x || ReadPpm( b ) is not { } y )
			return "pictures not compared (not P6 PPM)";

		if ( x.W != y.W || x.H != y.H )
			return $"pictures differ in size ({x.W}, {x.H}) ({y.W}, {y.H})";

		long n = (long)x.W * x.H, differ = 0, over = 0, sum = 0;
		var worst = 0;

		for ( long i = 0; i < n; i++ )
		{
			var top = 0;
			for ( var c = 0; c < 3; c++ )
			{
				var d = Math.Abs( x.Rgb[i * 3 + c] - y.Rgb[i * 3 + c] );
				sum += d;
				top = Math.Max( top, d );
			}

			if ( top > 0 ) differ++;
			if ( top > 16 ) over++;
			worst = Math.Max( worst, top );
		}

		var mean = sum / (3.0 * n);
		return string.Format( Inv, "pixels differ {0:F3} %, over 16 steps {1:F3} %, worst {2}, mean {3:F3}",
			100.0 * differ / n, 100.0 * over / n, worst, mean );
	}

	static string Delta( double? a, double? b )
	{
		if ( a is not { } va || b is not { } vb || va == 0 )
			return "".PadLeft( 8 );

		return ((100.0 * (vb - va) / va).ToString( "+0.0;-0.0;+0.0", Inv ) + "%").PadLeft( 8 );
	}

	static void SideBySide( Dictionary<string, Row> rowsA, Dictionary<string, Row> rowsB, Dictionary<string, string> pics, string labelA = "A", string labelB = "B" )
	{
		static string Cut( string s ) => s[..Math.Min( 10, s.Length )];
		Console.WriteLine( $"{"scene",-16} {"",6} {Cut( labelA ),10} {Cut( labelB ),10} {"change",8}" );

		var keys = new (string Key, int Digits)[] { ("fps", 0), ("typical", 2), ("gpu", 2) }.Concat( Zones.Select( z => (z, 3) ) ).ToArray();

		foreach ( var name in rowsA.Keys.Union( rowsB.Keys ).OrderBy( n => n, StringComparer.Ordinal ) )
		{
			var a = rowsA.GetValueOrDefault( name );
			var b = rowsB.GetValueOrDefault( name );

			foreach ( var (key, digits) in keys )
			{
				var va = a?.Get( key );
				var vb = b?.Get( key );
				if ( va is null && vb is null )
					continue;

				var label = key == "fps" ? name : "";
				Console.WriteLine( $"{label,-16} {key[..Math.Min( 6, key.Length )],-6} {Fmt( va, 10, digits )} {Fmt( vb, 10, digits )} {Delta( va, vb )}" );
			}

			if ( pics.TryGetValue( name, out var pic ) )
				Console.WriteLine( $"{"",-16} {pic}" );
		}
	}

	static Dictionary<string, Row> RowsOf( string folder ) => ScenesIn( folder ).ToDictionary( n => n, n => Parse( Path.Combine( folder, n + ".txt" ) ) );

	static void Compare( string folderA, string folderB )
	{
		var rowsA = RowsOf( folderA );
		var rowsB = RowsOf( folderB );
		var pics = rowsA.Keys.Intersect( rowsB.Keys ).ToDictionary( n => n,
			n => Pixels( Path.Combine( folderA, n + ".check.ppm" ), Path.Combine( folderB, n + ".check.ppm" ) ) );

		SideBySide( rowsA, rowsB, pics, Path.GetFileName( folderA.TrimEnd( '/' ) ), Path.GetFileName( folderB.TrimEnd( '/' ) ) );
	}

	//Each scene's numbers averaged over the rounds of one side
	static (Dictionary<string, Row> Rows, int Rounds) Averaged( string root, string side )
	{
		var rounds = Directory.GetDirectories( root, side + "-*" ).OrderBy( d => d, StringComparer.Ordinal ).ToList();
		var sums = new Dictionary<string, Dictionary<string, List<double>>>();

		foreach ( var folder in rounds )
		{
			foreach ( var name in ScenesIn( folder ) )
			{
				if ( Parse( Path.Combine( folder, name + ".txt" ) ) is not { } row )
					continue;

				var scene = sums.TryGetValue( name, out var s ) ? s : sums[name] = new();
				foreach ( var (key, value) in row.Values )
					(scene.TryGetValue( key, out var list ) ? list : scene[key] = new()).Add( value );
			}
		}

		var rows = sums.ToDictionary( kv => kv.Key, kv => new Row { Values = kv.Value.ToDictionary( v => v.Key, v => v.Value.Average() ) } );
		return (rows, rounds.Count);
	}

	static int Ab( string root, string specA, string specB )
	{
		var (rowsA, rounds) = Averaged( root, "A" );
		var (rowsB, _) = Averaged( root, "B" );

		Console.WriteLine( $"A = {specA}\nB = {specB}\n{rounds} round(s) each, interleaved A B A B; averages\n" );

		var pics = rowsA.Keys.Intersect( rowsB.Keys ).ToDictionary( n => n,
			n => Pixels( Path.Combine( root, "A-1", n + ".check.ppm" ), Path.Combine( root, "B-1", n + ".check.ppm" ) ) );
		SideBySide( rowsA, rowsB, pics );

		var failed = 0;
		foreach ( var side in new[] { "A", "B" } )
		{
			var folder = Path.Combine( root, side + "-1" );
			if ( !Directory.Exists( folder ) )
				continue;

			foreach ( var name in ScenesIn( folder ) )
			{
				var (state, _) = CheckOf( folder, name );
				if ( !Failed( state ) )
					continue;

				Console.WriteLine( $"{side} {name}: {state}" );
				failed++;
			}
		}

		return failed;
	}
}
