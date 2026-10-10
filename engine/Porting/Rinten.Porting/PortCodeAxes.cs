using System.IO;
using System.Text.RegularExpressions;

namespace Rinten.Porting;

/// <summary>The axes a project keeps in its code rather than in its scenes.</summary>
public static class PortCodeAxes
{
	//The type whose components mean a direction in the world.
	private const string Vector = "Vector3";

	//Where each of their components lands in ours, with sign, derived from the basis
	private static (int Component, bool Negative)[] Landing( ForeignBasis axes )
	{
		var theirs = new[] { axes.AxisX, axes.AxisY, axes.AxisZ };
		var landing = new (int, bool)[3];

		for ( var i = 0; i < 3; i++ )
		{
			var by = new[] { theirs[i].x, theirs[i].y, theirs[i].z };
			var at = 0;

			for ( var j = 1; j < 3; j++ )
			{
				if ( MathF.Abs( by[j] ) > MathF.Abs( by[at] ) ) at = j;
			}

			//Only a signed permutation can be done to a name. A basis that sends an axis somewhere
			//between two of ours has to be done to the numbers, and there are no numbers here.
			if ( MathF.Abs( MathF.Abs( by[at] ) - 1.0f ) > 0.001f ) return null;

			for ( var j = 0; j < 3; j++ )
			{
				if ( j != at && MathF.Abs( by[j] ) > 0.001f ) return null;
			}

			landing[i] = ( at, by[at] < 0.0f );
		}

		return landing;
	}

	private const string Components = "xyz";
	private const string Capitals = "XYZ";

	/// <summary>Turns every component name in the project's code onto our axes.</summary>
	public static int RewriteProject( IReadOnlyList<(string Path, string Named)> files,
		ForeignBasis axes, PortReport report )
	{
		if ( axes.IsIdentity ) return 0;

		var landing = Landing( axes );

		if ( landing is null )
		{
			report.Notes.Add( $"axes: {axes.Name} is not a turn a component name can carry - " +
				"the code's own x, y and z were left alone" );

			return 0;
		}

		var read = new List<(string Path, string Named, string[] Lines, string[] Code)>();

		foreach ( var (path, named) in files )
		{
			string[] lines;

			try { lines = File.ReadAllLines( path ); }
			catch ( Exception e )
			{
				report.Skipped.Add( $"{named}: could not read to turn its axes - {e.Message}" );
				continue;
			}

			read.Add( ( path, named, lines, [.. lines.Select( PortCodeUnits.CodeOnly )] ) );
		}

		if ( read.Count == 0 ) return 0;

		var project = PortCodeProject.Read( [.. read.Select( x => ( x.Named, x.Code ) )] );
		var changed = 0;

		foreach ( var file in read )
		{
			int count;

			//Never fail the whole port on one file: the file is already copied, worst case its axes stay theirs
			try
			{
				count = Rewrite( file.Lines, file.Code, project, landing );
			}
			catch ( Exception e )
			{
				report.Skipped.Add( $"{file.Named}: could not turn its axes - {e.Message}" );
				continue;
			}

			if ( count == 0 ) continue;

			try { File.WriteAllLines( file.Path, file.Lines ); }
			catch ( Exception e )
			{
				report.Skipped.Add( $"{file.Named}: could not write turned axes - {e.Message}" );
				continue;
			}

			changed += count;
			report.Notes.Add( $"{file.Named}: {count} vector component(s) turned from {axes.Name}" );
		}

		if ( changed > 0 )
		{
			report.Notes.Add( "axes: a vector this could not type kept its components - a Vector3 the " +
				"project never declares, or one handed back by something of ours this does not know. " +
				"Those are the lines to read if something still moves the wrong way." );
		}

		return changed;
	}

	private static readonly Regex Inferred = new(
		@"\bvar\s+(?<name>@?[A-Za-z_]\w*)\s*=\s*(?<init>[^;]+);", RegexOptions.Compiled );

	private static readonly Regex Term = new(
		@"^[\s\(\-\+!]*(?<term>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)", RegexOptions.Compiled );

	//File locals, with var types inferred from the first term of the expression (two rounds)
	private static Dictionary<string, string> Typed( string[] code, PortCodeProject project )
	{
		var locals = new Dictionary<string, string>( PortCodeProject.Locals( code ), StringComparer.Ordinal );

		for ( var round = 0; round < 2; round++ )
		{
			foreach ( var line in code )
			{
				foreach ( Match match in Inferred.Matches( line ) )
				{
					var name = match.Groups["name"].Value.TrimStart( '@' );

					if ( locals.ContainsKey( name ) ) continue;

					var first = Term.Match( match.Groups["init"].Value );

					if ( !first.Success ) continue;

					if ( IsVectorType( first.Groups["term"].Value, project, locals ) )
						locals[name] = Vector;
				}
			}
		}

		return locals;
	}

	//One file, in place.
	private static int Rewrite( string[] lines, string[] code, PortCodeProject project,
		(int Component, bool Negative)[] landing )
	{
		var locals = Typed( code, project );
		//The names this file fills from one of ours, worked out the way the units pass works out which of them hold metres - see PortCodeUnits.Metres,
		//which is the same question about the same boundary and is answered by following assignments rather than by reading names.
		var measured = PortCodeUnits.Metres( code, project, locals, new HashSet<string>( StringComparer.Ordinal ) );

		//And the ones it fills with a direction, which measure nothing and so are invisible to the pass above - see EngineSpace.
		var engineHeld = EngineSpace( code, project, locals, measured );
		var changed = 0;

		for ( var i = 0; i < lines.Length; i++ )
		{
			var turned = Turn( lines[i], project, locals, landing, ref changed, statement: true, engineHeld );

			if ( turned == lines[i] ) continue;

			lines[i] = turned;
			code[i] = PortCodeUnits.CodeOnly( turned );
		}

		return changed;
	}

	//Renames components in code, skipping comments/strings, right to left; recurses into arguments
	private static string Turn( string text, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals, (int Component, bool Negative)[] landing,
		ref int changed, bool statement, HashSet<string> engineHeld )
	{
		var code = PortCodeUnits.CodeOnly( text );
		var sites = Sites( code, project, locals, landing, statement, engineHeld );

		if ( sites is null || sites.Count == 0 ) return text;

		for ( var s = sites.Count - 1; s >= 0; s-- )
		{
			var site = sites[s];
			var written = Written( text, site, project, locals, landing, ref changed, engineHeld );

			if ( written is null ) continue;

			text = text[..site.Start] + written + text[( site.Start + site.Length )..];
			changed++;
		}

		return text;
	}

	//What kind of thing a site is.
	private enum Kind
	{
		/// <summary>A component read: <c>v.z</c>.</summary>
		Component,

		/// <summary>A component assigned to: <c>v.z = a;</c>.</summary>
		Assigned,

		/// <summary>A component replaced: <c>v.WithZ( 0 )</c>.</summary>
		With,

		/// <summary>Three components written out: <c>new Vector3( a, b, c )</c>.</summary>
		Made,
	}

	//One place to turn. Inside is where its arguments are, in the same text the site was found in,
	//so the writing takes them from the real thing rather than from the blanked copy the scan reads.
	private readonly record struct Site( int Start, int Length, Kind Kind, int Component,
		string Receiver, int Inside, int Held, string Operator );

	private static readonly Regex Access = new(
		@"(?<recv>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)\s*\.\s*(?<c>[xyz])\b(?!\s*[\(\w])",
		RegexOptions.Compiled );

	private static readonly Regex With = new(
		@"(?<recv>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)\s*\.\s*With(?<c>[XYZ])\s*\(",
		RegexOptions.Compiled );

	private static readonly Regex Made = new( @"new\s+Vector3\s*\(", RegexOptions.Compiled );

	//Outermost world-direction sites in text; null if any can't be turned, since half-turned is worse
	private static List<Site> Sites( string code, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals, (int Component, bool Negative)[] landing,
		bool statement, HashSet<string> engineHeld )
	{
		var found = new List<Site>();

		foreach ( Match match in Made.Matches( code ) )
		{
			var list = PortCodeUnits.ListAt( code, match.Index + match.Length - 1 );

			//A list that does not close on this line - ListAt answers with a start of -1 for that,
			//and a length of nought is a list with nothing in it. Neither is three components.
			if ( list.Start < 0 || list.Length <= 0 ) continue;

			//Two arguments is a Vector3 built out of a Vector2 and a number and one is a fill or a copy: neither is three components in their order.
			if ( Arguments( code.Substring( list.Start, list.Length ) ) is not { Count: 3 } ) continue;

			//And only where what is built goes to this engine, by either of the two ways a value crosses that line: put into something of ours, or handed to
			//something of ours. A game filling its own triple - a sway, a recoil pattern, a spread - writes the same three arguments and means its own frame by them.
			var given = Handed( code, match.Index, project, locals );
			var into = Target( code, match.Index );

			if ( !given && ( into is null
				|| !IsEngineVector( code, match.Index, into, project, locals, engineHeld ) ) ) continue;

			found.Add( new Site( match.Index, list.Start + list.Length + 1 - match.Index,
				Kind.Made, 0, "", list.Start, list.Length, "" ) );
		}

		foreach ( Match match in With.Matches( code ) )
		{
			if ( !IsEngineVector( code, match.Index, match.Groups["recv"].Value, project, locals,
				engineHeld ) ) continue;

			var list = PortCodeUnits.ListAt( code, match.Index + match.Length - 1 );

			//The same: a WithZ whose argument runs onto the next line is left for a person to read.
			if ( list.Start < 0 || list.Length < 0 ) continue;

			found.Add( new Site( match.Index, list.Start + list.Length + 1 - match.Index, Kind.With,
				Capitals.IndexOf( match.Groups["c"].Value[0] ), match.Groups["recv"].Value,
				list.Start, list.Length, "" ) );
		}

		foreach ( Match match in Access.Matches( code ) )
		{
			var recv = match.Groups["recv"].Value;

			if ( !IsEngineVector( code, match.Index, recv, project, locals, engineHeld ) ) continue;

			var component = Components.IndexOf( match.Groups["c"].Value[0] );
			var negative = landing[component].Negative;
			var after = match.Index + match.Length;

			if ( statement && Assignment( code, after ) is { } written )
			{
				var ends = code.IndexOf( ';', written.Value );

				//Its value is on another line, and the sign this needs cannot be put anywhere from here.
				if ( ends < 0 ) return negative ? null : found;

				found.Add( new Site( match.Index, ends + 1 - match.Index, Kind.Assigned, component,
					recv, written.Value, ends - written.Value, written.Operator ) );

				continue;
			}

			if ( negative && Steps( code, after ) ) return null;

			found.Add( new Site( match.Index, match.Length, Kind.Component, component, recv, 0, 0, "" ) );
		}

		//Outermost only: one inside another is the recursion's to do, and doing it here as well would turn it twice.
		found.Sort( ( a, b ) => a.Start.CompareTo( b.Start ) );

		var outer = new List<Site>();

		foreach ( var site in found )
		{
			if ( outer.Count > 0 )
			{
				var last = outer[^1];

				if ( site.Start >= last.Start && site.Start < last.Start + last.Length ) continue;
			}

			outer.Add( site );
		}

		return outer;
	}

	//The site, written in our axes - or null where it is left alone.
	private static string Written( string text, Site site, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals, (int Component, bool Negative)[] landing,
		ref int changed, HashSet<string> engineHeld )
	{
		var (component, negative) = landing[site.Component];
		var inner = 0;

		//A local function cannot take the count by reference, so it keeps its own and it is added on at the end - every path below returns.
		string Inside( int at, int held )
		{
			var was = inner;
			var turned = Turn( text.Substring( at, held ), project, locals, landing, ref was,
				statement: false, engineHeld );

			inner = was;

			return turned;
		}

		switch ( site.Kind )
		{
			case Kind.Component:
			{
				var written = $"{site.Receiver}.{Components[component]}";

				return negative ? $"(-{written})" : written;
			}

			case Kind.Assigned:
			{
				var value = Inside( site.Inside, site.Held ).Trim();

				changed += inner;

				//The sign goes into the value: v.x = a is v.z = -( a ), because the name on this side of an equals sign cannot carry one.
				return $"{site.Receiver}.{Components[component]} {site.Operator} " +
					( negative ? $"-( {value} );" : $"{value};" );
			}

			case Kind.With:
			{
				var argument = Inside( site.Inside, site.Held );

				changed += inner;

				return $"{site.Receiver}.With{Capitals[component]}(" +
					( negative ? $" -( {argument.Trim()} ) " : argument ) + ")";
			}

			case Kind.Made:
			{
				var parts = Spans( text, site.Inside, site.Held );

				if ( parts is not { Count: 3 } ) return null;

				var ours = new string[3];

				for ( var i = 0; i < 3; i++ )
				{
					var (lands, flips) = landing[i];
					var written = Inside( parts[i].Start, parts[i].Length ).Trim();

					ours[lands] = flips ? $"-( {written} )" : written;
				}

				changed += inner;

				return $"new Vector3( {ours[0]}, {ours[1]}, {ours[2]} )";
			}
		}

		return null;
	}

	//The assignment operator right after a component, or null where what follows is not one.
	private static (string Operator, int Value)? Assignment( string code, int after )
	{
		var at = after;

		while ( at < code.Length && char.IsWhiteSpace( code[at] ) ) at++;

		if ( at >= code.Length ) return null;

		//A comparison, a lambda, or the tail of one of those.
		if ( at > 0 && code[at - 1] is '=' or '!' or '<' or '>' ) return null;

		if ( code[at] == '=' )
		{
			return at + 1 < code.Length && code[at + 1] is '=' or '>' ? null : ( "=", at + 1 );
		}

		//Where the value starts is asked for rather than worked out from the operator's length: there is whitespace between the name and the
		//operator as often as not, and a value measured from the wrong end begins with the equals sign - "point.y -= = wasOnGround ? ...".
		if ( code[at] is '+' or '-' or '*' or '/' && at + 1 < code.Length && code[at + 1] == '=' )
			return ( code[at..( at + 2 )], at + 2 );

		return null;
	}

	//Whether a component is stepped up or down where it stands.
	private static bool Steps( string code, int after )
	{
		var at = after;

		while ( at < code.Length && char.IsWhiteSpace( code[at] ) ) at++;

		return at + 1 < code.Length && code[at..( at + 2 )] is "++" or "--";
	}

	//Where each top level argument of a list is.
	private static List<(int Start, int Length)> Spans( string text, int at, int held )
	{
		var parts = new List<(int, int)>();
		var depth = 0;
		var last = at;

		for ( var i = at; i < at + held; i++ )
		{
			var c = text[i];

			if ( c is '(' or '[' ) depth++;
			else if ( c is ')' or ']' ) depth--;
			else if ( c == ',' && depth == 0 )
			{
				parts.Add( ( last, i - last ) );
				last = i + 1;
			}
		}

		parts.Add( ( last, at + held - last ) );

		foreach ( var (start, length) in parts )
		{
			if ( length <= 0 || string.IsNullOrWhiteSpace( text.Substring( start, length ) ) ) return null;
		}

		return parts;
	}

	//The top level arguments of a list, or null where it is not that.
	private static List<string> Arguments( string inside )
	{
		var spans = Spans( inside, 0, inside.Length );

		return spans?.Select( x => inside.Substring( x.Start, x.Length ) ).ToList();
	}

	private static readonly Regex Opens = new(
		@"(?:new\s+(?<type>@?[A-Za-z_]\w*)|(?<recv>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)\s*\.\s*(?<call>@?[A-Za-z_]\w*))\s*\($",
		RegexOptions.Compiled );

	//Whether the value built here is handed to the engine (our constructor or a call not in the project)
	private static bool Handed( string code, int at, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals )
	{
		//What opens the list this sits in: back past whatever is already balanced, to the bracket nobody closed.
		var depth = 0;

		for ( var i = at - 1; i >= 0; i-- )
		{
			var c = code[i];

			if ( c is ')' or ']' ) depth++;
			else if ( c is '(' or '[' )
			{
				if ( depth > 0 ) { depth--; continue; }

				var open = Opens.Match( code[..( i + 1 )] );

				if ( !open.Success ) return false;

				if ( open.Groups["type"].Success )
					return !project.Types.Contains( open.Groups["type"].Value );

				return project.EngineCall( code, open.Groups["call"].Index,
					open.Groups["call"].Value, locals );
			}
		}

		return false;
	}

	private static readonly Regex Basis = new(
		@"^(?<recv>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)\s*\.\s*(?<name>Forward|Backward|Left|Right|Up|Down)\b",
		RegexOptions.Compiled );

	//Names this file fills with engine basis vectors (Forward, Up...), unless the project declares them
	private static HashSet<string> EngineSpace( string[] code, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals, HashSet<string> measured )
	{
		var space = new HashSet<string>( measured, StringComparer.Ordinal );

		for ( var round = 0; round < 3; round++ )
		{
			var before = space.Count;

			foreach ( var line in code )
			{
				foreach ( Match assigned in Assigns.Matches( line ) )
				{
					var name = Last( assigned.Groups["name"].Value );

					if ( space.Contains( name ) ) continue;
					if ( !IsVectorType( name, project, locals ) ) continue;

					var value = line[( assigned.Index + assigned.Length )..].TrimStart( ' ', '\t', '(' );

					if ( !FromEngine( value, project, locals, space ) ) continue;

					space.Add( name );
				}
			}

			if ( space.Count == before ) break;
		}

		return space;
	}

	//Whether the project declares a vector of its own by this name.
	private static bool Declares( PortCodeProject project, string name )
	{
		if ( !project.Members.Contains( name ) ) return false;

		return project.MemberTypes.TryGetValue( name, out var type ) && IsNamed( type );
	}

	//Whether an expression begins with one of this engine's vectors.
	private static bool FromEngine( string value, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals, HashSet<string> space )
	{
		var basis = Basis.Match( value );

		//Only basis vectors seed this (not Zero), unless the project declares a direction of that name
		if ( basis.Success && !Declares( project, basis.Groups["name"].Value ) ) return true;

		var first = Term.Match( value );

		if ( !first.Success ) return false;

		var term = Last( first.Groups["term"].Value );

		return space.Contains( term ) && !Made.IsMatch( value );
	}

	//The last name of a receiver chain, which is the one that is typed.
	private static string Last( string receiver )
	{
		var dot = receiver.LastIndexOf( '.' );
		var last = dot >= 0 ? receiver[( dot + 1 )..] : receiver;

		return last.Trim().TrimStart( '@' );
	}

	//What a statement assigns to, at or before this point.
	private static string Target( string code, int at )
	{
		var matches = Assigns.Matches( code[..at] );

		return matches.Count == 0 ? null : Last( matches[^1].Groups["name"].Value );
	}

	private static readonly Regex Assigns = new(
		@"(?<name>@?[A-Za-z_]\w*(?:\s*\.\s*@?[A-Za-z_]\w*)*)\s*(?:=|\+=|-=)(?!=)",
		RegexOptions.Compiled );

	//Whether a name is written down as one of these at all.
	private static bool IsVectorType( string name, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals )
	{
		name = Last( name );

		if ( locals.TryGetValue( name, out var local ) ) return IsNamed( local );

		return project.MemberTypes.TryGetValue( name, out var member ) && IsNamed( member );
	}

	//Whether the receiver yields an engine-space vector; the game's own triples (sway, recoil) stay
	private static bool IsEngineVector( string code, int at, string receiver,
		PortCodeProject project, IReadOnlyDictionary<string, string> locals,
		HashSet<string> engineHeld )
	{
		var last = Last( receiver );

		if ( last.Length == 0 ) return false;

		//Must be declared a vector type; Vector2, Angles and Rotation components are not axes
		if ( !IsVectorType( last, project, locals ) ) return false;

		//This engine's, either because the project never declared it - so it is ours by elimination, a WorldPosition
		//or a Rotation.Forward - or because what the project declared has been followed back to one of ours.
		if ( engineHeld.Contains( last ) ) return true;

		return project.EngineRead( code, at, last, locals );
	}

	//Whether a written type is the one whose components are axes.
	private static bool IsNamed( string type )
	{
		if ( string.IsNullOrWhiteSpace( type ) ) return false;

		type = type.Trim().TrimEnd( '?' );

		var dot = type.LastIndexOf( '.' );

		if ( dot >= 0 ) type = type[( dot + 1 )..];

		return string.Equals( type, Vector, StringComparison.Ordinal );
	}
}
