using System.IO;
using System.Text.RegularExpressions;

namespace Rinten.Porting;

/// <summary>The measurements a project keeps in its code rather than in its scenes.</summary>
public static class PortCodeUnits
{
	//Line with comments and strings blanked, same length so match offsets still point at real text
	internal static string CodeOnly( string line )
	{
		var chars = line.ToCharArray();
		var inString = false;
		var inChar = false;

		for ( var i = 0; i < chars.Length; i++ )
		{
			if ( !inString && !inChar && i + 1 < chars.Length && chars[i] == '/' && chars[i + 1] == '/' )
			{
				for ( var j = i; j < chars.Length; j++ ) chars[j] = ' ';
				break;
			}

			if ( chars[i] == '\\' && ( inString || inChar ) ) { i++; continue; }

			if ( chars[i] == '"' && !inChar ) { inString = !inString; chars[i] = ' '; continue; }
			if ( chars[i] == '\'' && !inString ) { inChar = !inChar; chars[i] = ' '; continue; }

			if ( inString || inChar ) chars[i] = ' ';
		}

		return new string( chars );
	}

	//Every identifier in an expression, to ask each where it came from.
	private static readonly Regex Word = new( @"[A-Za-z_]\w*", RegexOptions.Compiled );

	//Named-argument prefix (transform: x), not part of the value; wrapping it would make a tuple
	private static readonly Regex Named = new( @"^\s*[A-Za-z_]\w*\s*:(?!:)\s*", RegexOptions.Compiled );

	//An argument that declares something rather than handing a value over. BuildStrip( mesh, out float maxHalf ) is a place the callee writes to. There
	//is no value here to convert, and there is nowhere to put the multiplication either: (out float maxHalf) * 0.0254f is not an expression at all.
	private static readonly Regex WrittenTo = new( @"^\s*(?:out|ref|in)\b", RegexOptions.Compiled );

	//An array being made, which mentions a type without being a value of it. new Transform[count] names one of the types this engine
	//measures and holds none of them yet. Multiplied, it is an array times a float, which is not an operator anyone has written.
	private static readonly Regex Allocates = new(
		@"^\s*new\s+[\w\.]+(?:<[^<>]*>)?\s*\[", RegexOptions.Compiled );

	//Zero in any spelling (default, new T(), null, 0f): nothing to convert, and defaults can't take one
	private static readonly Regex Nought = new(
		@"^\s*(?:null|default(?:\s*\([^()]*\))?|new\s+[\w\.]*(?:<[^<>]*>)?\s*\(\s*\)|" +
		@"new\s*\(\s*\)|[-+]?0*\.?0+[fFdDmM]?|[-+]?0+\.?0*[fFdDmM]?)\s*$",
		RegexOptions.Compiled );

	//A number written down, and nothing else. The only kind of default there is
	//anything to convert in - see Defaults for why the language leaves nothing else.
	private static readonly Regex Number = new(
		@"^\s*[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?[fFdDmM]?\s*$", RegexOptions.Compiled );

	//A constant taken out of a type - the shape every enum is written in.
	private static readonly Regex Constant = new(
		@"^\s*(?<type>[A-Z]\w*)\s*\.\s*[A-Za-z_]\w*\s*$", RegexOptions.Compiled );

	//Nested-type constant (A.B.C with a capital first), never a measurement
	private static readonly Regex Nested = new(
		@"^\s*[A-Z]\w*\s*\.\s*[A-Za-z_]\w*\s*\.\s*[A-Za-z_]\w*\s*$", RegexOptions.Compiled );

	//Boolean-shaped value; never a measurement, multiplying it doesn't compile
	private static readonly Regex Boolean = new(
		@"^\s*!(?!=)|\|\||&&|^\s*(?:true|false)\s*$|[<>]=?|[!=]=", RegexOptions.Compiled );

	//Whether text is a list or multi-declaration (float u = a, v = b), so not one expression
	private static bool Splits( string text )
	{
		var depth = 0;

		for ( var i = 0; i < text.Length; i++ )
		{
			var c = text[i];

			if ( c is '(' or '[' ) { depth++; continue; }
			if ( c is ')' or ']' ) { depth--; continue; }

			if ( depth > 0 ) continue;

			if ( c == ',' ) return true;

			if ( c != '=' ) continue;

			//An assignment rather than a comparison.
			var before = i > 0 ? text[i - 1] : ' ';
			var after = i + 1 < text.Length ? text[i + 1] : ' ';

			if ( before is '<' or '>' or '!' or '=' || after == '=' ) continue;

			return true;
		}

		return false;
	}

	//Whether brackets balance in text; half of a multi-line call is left alone, whole terms still convert
	private static bool Balanced( string text )
	{
		var depth = 0;

		foreach ( var c in text )
		{
			if ( c is '(' or '[' ) depth++;
			else if ( c is ')' or ']' ) depth--;

			if ( depth < 0 ) return false;
		}

		return depth == 0;
	}

	//Value assigned to an engine-measured member (collider.Radius = size); dimensions from SceneUnits
	private static readonly Regex GivenTo = new(
		@"\.(?<member>[A-Za-z_]\w*)\s*(?:=(?!=)|\+=|-=)\s*(?<value>[^;]+);",
		RegexOptions.Compiled );

	//Same without a dot (WorldPosition = ...); only if the project doesn't declare that name itself
	private static readonly Regex GivenBare = new(
		@"^\s*(?<member>[A-Za-z_]\w*)\s*(?:=(?!=)|\+=|-=)\s*(?<value>[^;]+);",
		RegexOptions.Compiled );

	//A name being given a value, for following where that value came from.
	internal static readonly Regex Bound = new(
		@"(?:^|[^\w.])(?:var|float|double|Vector2|Vector3|Rotation|Angles|Transform|BBox)\s+" +
		@"(?<name>[A-Za-z_]\w*)\s*=(?!=)\s*(?<value>[^;]+);",
		RegexOptions.Compiled );

	//Engine measurement compared with a literal (Velocity.Length >= 200); the literal must convert
	internal static readonly Regex Compared = new(
		@"(?<left>[A-Za-z_][\w\.]*)\s*(?<op>[<>]=?|==|!=)\s*(?<value>-?\d+(?:\.\d+)?[fdFD]?)(?![\w.])",
		RegexOptions.Compiled );

	//Comparison against a name; only learnt from, never rewritten (generics and null look the same)
	internal static readonly Regex ComparedName = new(
		@"(?<left>[A-Za-z_][\w\.]*)\s*(?<op>[<>]=?|==|!=)\s*" +
		@"(?!(?:null|true|false|default|new)\b)(?<value>[A-Za-z_]\w*)(?![\w.<(])",
		RegexOptions.Compiled );

	//A function being declared, so its parameters can be told apart from a call.
	private static readonly Regex Declares = new(
		@"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|private|protected|internal|static|readonly|const|" +
		@"virtual|override|new|sealed|abstract|partial|required|volatile|extern|unsafe|async)\s+)*" +
		@"(?!(?:new|return|if|while|for|foreach|using|else|await|throw|switch|lock|yield)\b)" +
		@"[\w<>\[\],\.\?]+\s+(?<name>[A-Za-z_]\w*)\s*(?=\()",
		RegexOptions.Compiled );

	//Contents of the brackets opening here, counting nesting (a regex stops at the first ')')
	internal static (int Start, int Length) ListAt( string code, int from )
	{
		var open = from;

		while ( open < code.Length && code[open] != '(' ) open++;

		if ( open >= code.Length ) return ( -1, 0 );

		var depth = 0;

		for ( var i = open; i < code.Length; i++ )
		{
			if ( code[i] is '(' or '[' ) depth++;
			else if ( code[i] is ')' or ']' )
			{
				depth--;

				if ( depth == 0 ) return ( open + 1, i - open - 1 );
			}
		}

		return ( -1, 0 );
	}

	/// <summary>Converts the units in every code file of a ported project.</summary>
	public static int RewriteProject( IReadOnlyList<(string Path, string Named)> files,
		PortUnits units, PortReport report )
	{
		if ( units.IsIdentity ) return 0;

		var read = new List<(string Path, string Named, string[] Lines, string[] Code)>();

		foreach ( var (path, named) in files )
		{
			string[] lines;

			try
			{
				lines = File.ReadAllLines( path );
			}
			catch ( Exception e )
			{
				report.Skipped.Add( $"{named}: could not read to convert its units - {e.Message}" );
				continue;
			}

			read.Add( ( path, named, lines, [.. lines.Select( CodeOnly )] ) );
		}

		if ( read.Count == 0 ) return 0;

		var project = PortCodeProject.Read( [.. read.Select( x => ( x.Named, x.Code ) )] );

		//What the scene pass needs from here: a type the game declares keeps the game's numbers, in its code and in
		//every prefab that holds one of its components. Converting only one of the two is worse than converting neither.
		foreach ( var type in project.Types ) SceneUnits.Owned.Add( type );

		var changed = 0;

		foreach ( var file in read )
		{
			var count = Rewrite( file.Lines, file.Code, project, units, file.Named, report );

			if ( count == 0 ) continue;

			try
			{
				File.WriteAllLines( file.Path, file.Lines );
			}
			catch ( Exception e )
			{
				report.Skipped.Add( $"{file.Named}: could not write converted units - {e.Message}" );
				continue;
			}

			changed += count;
			report.Notes.Add(
				$"{file.Named}: {count} number(s) in code multiplied through to {units.TargetUnitName}s" );
		}

		Explain( project, units, report );

		return changed;
	}

	/// <summary>Reads one file on its own. Kept for a caller that has one file and no project around it, and worse at its job than <see cref="RewriteProject"/> for exactly that reason.</summary>
	public static int RewriteFile( string path, string relative, PortUnits units, PortReport report )
		=> RewriteProject( [( path, relative.Replace( '\\', '/' ) )], units, report );

	//Says out loud what the port had to assume, because these are the lines somebody has to look at.
	private static void Explain( PortCodeProject project, PortUnits units, PortReport report )
	{
		foreach ( var name in project.Shared.OrderBy( x => x, StringComparer.Ordinal ) )
		{
			report.Notes.Add(
				$"units: this project declares its own '{name}', which is also what this engine " +
				$"calls something it measures - reads of it were taken to be the game's own " +
				$"units unless the receiver said which it was" );
		}

		foreach ( var (method, needs) in project.Needs.OrderBy( x => x.Key, StringComparer.Ordinal ) )
		{
			report.Notes.Add(
				$"units: {method}() is handed a measurement in argument(s) " +
				$"{string.Join( ", ", needs.Keys.OrderBy( x => x ) )} - converted at its callers " +
				$"rather than inside it" );
		}
	}

	private static int Rewrite( string[] lines, string[] code, PortCodeProject project, PortUnits units,
		string named, PortReport report )
	{
		var locals = PortCodeProject.Locals( code );
		var doubtful = new HashSet<string>( StringComparer.Ordinal );
		var metres = Metres( code, project, locals, doubtful );

		var changed = 0;

		//The parameters of whichever function the line is inside that the project pass says arrive in metres. Their conversion happened at the caller,
		//so inside here they are this engine's numbers and must be left alone - which is the whole reason a project is read before it is rewritten.
		var arrived = new HashSet<string>( StringComparer.Ordinal );

		for ( var i = 0; i < lines.Length; i++ )
		{
			if ( code[i].Trim().Length == 0 ) continue;

			var declares = Declares.Match( code[i] );

			if ( declares.Success )
			{
				var list = ListAt( code[i], declares.Index + declares.Length );
				var parameters = list.Start < 0
					? []
					: PortCodeProject.ParameterNames( code[i].Substring( list.Start, list.Length ) );

				arrived.Clear();

				if ( project.Needs.TryGetValue( declares.Groups["name"].Value, out var needs ) )
				{
					foreach ( var index in needs.Keys )
					{
						if ( index < parameters.Length ) arrived.Add( parameters[index] );
					}

					//A default is the only place that parameter's number can be converted: nobody wrote it at the call.
					changed += Defaults( lines, i, code[i], list, needs, units, named,
						declares.Groups["name"].Value, report );
				}

				//A method written as one expression has its body on this line, after the brackets. Everything before them is the declaration and has been dealt with.
				var after = list.Start < 0 ? code[i].Length : list.Start + list.Length + 1;

				if ( after >= code[i].Length ) continue;

				changed += Sites( lines, i, code[i], project, units, metres, arrived, locals, doubtful, after );

				continue;
			}

			changed += Sites( lines, i, code[i], project, units, metres, arrived, locals, doubtful, 0 );
		}

		return changed;
	}

	//Every place on this line where a number crosses into the engine, converted right to left so an edit never moves a place not yet read.
	private static int Sites( string[] lines, int i, string code, PortCodeProject project,
		PortUnits units, HashSet<string> metres, HashSet<string> arrived,
		IReadOnlyDictionary<string, string> locals, HashSet<string> doubtful, int from )
	{
		var sites = new List<(int Start, int Length, PortDimension Dimension)>();

		//Handed to one of this engine's calls - unless the project has a function of that name,
		//in which case it is the game's own and what it needs was worked out from its body.
		foreach ( var argument in PortCodeFlow.MeasuredArguments( code ) )
		{
			if ( !project.EngineCall( code, argument.At, argument.Call, locals ) ) continue;

			sites.Add( ( argument.Start, argument.Length, argument.Dimension ) );
		}

		//Handed to one of the game's own functions that passes it on to this engine. Converted here, at the outermost place the number is still the game's own.
		foreach ( var (method, needs) in project.Needs )
		{
			var spans = PortCodeFlow.ArgumentsOf( code, method );

			if ( spans.Count == 0 ) continue;

			foreach ( var (index, dimension) in needs )
			{
				if ( index >= spans.Count ) continue;

				sites.Add( ( spans[index].Start, spans[index].Length, dimension ) );
			}
		}

		//Held up against a measurement of this engine's.
		foreach ( Match match in Compared.Matches( code ) )
		{
			var left = match.Groups["left"];
			var dimension = Measured( code, left, project, metres, arrived, locals );

			if ( dimension is PortDimension.None or PortDimension.Unknown ) continue;

			var value = match.Groups["value"];

			sites.Add( ( value.Index, value.Length, dimension ) );
		}

		//Assigned to a name known to hold metres, local or member (var point = Position + Vector3.Down * 2)
		foreach ( Match bound in Bound.Matches( code ) )
		{
			if ( !metres.Contains( bound.Groups["name"].Value ) ) continue;
			if ( Counter( code, bound.Index ) ) continue;
			if ( doubtful.Contains( bound.Groups["name"].Value ) ) continue;

			//Declared as something no measurement fits in - a pixel width, a count, a flag - whatever this file made of the name elsewhere.
			if ( project.Unmeasurable( bound.Groups["name"].Value, locals ) ) continue;

			var value = bound.Groups["value"];

			sites.Add( ( value.Index, value.Length, PortDimension.Length ) );
		}

		//Given to a member this engine measures.
		foreach ( Match match in GivenTo.Matches( code ) ) Given( sites, code, match, project, locals, metres );

		var bare = GivenBare.Match( code );

		//A bare name is this engine's if the project does not declare it, or declares it and holds one of this engine's measurements in it.
		if ( bare.Success && ( !project.Members.Contains( bare.Groups["member"].Value )
			|| project.Metres.Contains( bare.Groups["member"].Value )
			|| metres.Contains( bare.Groups["member"].Value ) ) )
			Given( sites, code, bare, project, locals, metres );

		if ( sites.Count == 0 ) return 0;


		var changed = 0;
		var taken = new List<(int Start, int End)>();

		foreach ( var site in sites.OrderByDescending( x => x.Start ) )
		{
			if ( site.Length <= 0 || site.Start < from ) continue;

			//One number is handed over once. Where two sites overlap - a call inside an argument of another
			//- the inner one has already been converted and the outer one would be converting it again.
			if ( taken.Any( x => site.Start < x.End && site.Start + site.Length > x.Start ) ) continue;

			var expression = units.Expression( site.Dimension );

			if ( expression.Length == 0 ) continue;

			var count = Wrap( lines, i, code, ( site.Start, site.Length ),
				expression, units, project, metres, arrived, locals, doubtful );

			if ( count == 0 ) continue;

			taken.Add( ( site.Start, site.Start + site.Length ) );
			changed += count;
		}

		return changed;
	}

	private static void Given( List<(int Start, int Length, PortDimension Dimension)> sites,
		string code, Match match, PortCodeProject project, IReadOnlyDictionary<string, string> locals,
		HashSet<string> metres )
	{
		var member = match.Groups["member"];
		var value = match.Groups["value"];

		//An enum being set from its own type - "rb.RigidbodyFlags = RigidbodyFlags.DisableCollisionSounds". Nothing written that
		//way is a measurement, and the name having been learnt as one somewhere else in the project does not make this one.
		if ( Constant.Match( value.Value ) is { Success: true } constant
			&& constant.Groups["type"].Value == member.Value ) return;

		//Skip targets declared as types that can't hold a measurement (int, long, bool)
		if ( project.Unmeasurable( member.Value, locals ) ) return;

		//A component of a metres name (point.z -= x) counts; other members like Enabled don't
		if ( metres.Contains( member.Value )
			|| ( Component.Contains( member.Value ) && metres.Contains( Before( code, member.Index ) ) ) )
		{
			Handed( sites, code, ( value.Index, value.Length ), PortDimension.Length );
			return;
		}

		//A member the project declares is the game's own storage, in the game's own units - and a number going into it is not crossing anything.
		if ( !project.EngineRead( code, member.Index, member.Value, locals ) ) return;

		var dimension = project.MetreOf.TryGetValue( member.Value, out var held )
			? held
			: project.DimensionOf( code, member.Index, member.Value, locals );

		if ( dimension is PortDimension.None or PortDimension.Unknown ) return;

		Handed( sites, code, ( value.Index, value.Length ), dimension );
	}

	//Calls that only carry their arguments through, so the boundary is each argument rather than the call.
	private static readonly string[] Carries = [ "Clamp", "Min", "Max" ];

	//The names one part of a vector goes by, which are the only members of a measurement that are themselves a measurement.
	private static readonly HashSet<string> Component = new( StringComparer.Ordinal )
	{
		"x", "y", "z", "w", "X", "Y", "Z", "W",
	};

	//The same, except for the last argument: an interpolation ends with how far between the two to go, and a fraction is not a length.
	private static readonly string[] CarriesButLast = [ "Lerp", "LerpTo", "Slerp" ];

	//Splits a handed value into sites, opening pass-through calls like Vector3.Lerp( a, b, t )
	private static void Handed( List<(int Start, int Length, PortDimension Dimension)> sites,
		string code, (int Start, int Length) value, PortDimension dimension )
	{
		var text = code.Substring( value.Start, value.Length ).Trim();
		var open = text.IndexOf( '(' );

		if ( open > 0 && text.EndsWith( ")", StringComparison.Ordinal ) )
		{
			var called = text[..open].TrimEnd();
			var dot = called.LastIndexOf( '.' );
			var name = dot < 0 ? called : called[( dot + 1 )..];

			var all = Carries.Contains( name );

			if ( all || CarriesButLast.Contains( name ) )
			{
				var list = ListAt( code, value.Start );

				if ( list.Start > 0 )
				{
					var arguments = Split( code, list.Start, list.Length );
					var last = all ? arguments.Count : arguments.Count - 1;

					for ( var at = 0; at < last; at++ )
					{
						if ( arguments[at].Length > 0 )
							sites.Add( ( arguments[at].Start, arguments[at].Length, dimension ) );
					}

					return;
				}
			}
		}

		sites.Add( ( value.Start, value.Length, dimension ) );
	}

	//The name written immediately before the dot at this position.
	private static string Before( string code, int at )
	{
		var i = at - 1;

		while ( i >= 0 && code[i] == ' ' ) i--;
		if ( i < 0 || code[i] != '.' ) return "";

		i--;

		var end = i + 1;

		while ( i >= 0 && ( char.IsLetterOrDigit( code[i] ) || code[i] == '_' ) ) i--;

		return end > i + 1 ? code[( i + 1 )..end] : "";
	}

	//Converts defaults of parameters that need metres; only literal numbers, since defaults must be
	//constant. default/new T() mean zero anyway; anything else is reported
	private static int Defaults( string[] lines, int i, string code, (int Start, int Length) list,
		Dictionary<int, PortDimension> needs, PortUnits units, string named, string method,
		PortReport report )
	{
		if ( list.Start < 0 ) return 0;

		var spans = Split( code, list.Start, list.Length );
		var changed = 0;

		foreach ( var (index, dimension) in needs.OrderByDescending( x => x.Key ) )
		{
			if ( index >= spans.Count ) continue;

			var expression = units.Expression( dimension );

			if ( expression.Length == 0 ) continue;

			var part = code.Substring( spans[index].Start, spans[index].Length );
			var equals = part.IndexOf( '=', StringComparison.Ordinal );

			if ( equals < 0 ) continue;

			var start = spans[index].Start + equals + 1;
			var length = spans[index].Length - equals - 1;
			var written = lines[i].Substring( start, length ).Trim();

			if ( written.Length == 0 ) continue;
			if ( written.Contains( units.ConversionExpression, StringComparison.Ordinal ) ) continue;

			//Nought, whichever of the four ways it is written. Nothing to convert, and nowhere to put it if there were.
			if ( Nought.IsMatch( written ) ) continue;

			//Non-literal defaults can't be multiplied and stay constant: report them, unless typed numeric
			if ( !Number.IsMatch( written ) && !Numeric.Contains( TypeOf( part[..equals] ) ) )
			{
				report.Notes.Add(
					$"units: {named}:{i + 1} {method}() takes a measurement whose default is " +
					$"'{written}' - a default has to be a constant, so the conversion could not " +
					$"be written there and the number is still in {units.SourceUnitName}s" );

				continue;
			}

			lines[i] = lines[i][..start] + $" ({written}) * {expression}" + lines[i][( start + length )..];

			++changed;
		}

		return changed;
	}

	//The types whose constants are all numbers. Only used where the language has already promised the value is a constant - see
	//Defaults. Anywhere else a name says nothing about what is in it, which is the mistake this file's history is mostly made of.
	private static readonly HashSet<string> Numeric = new( StringComparer.Ordinal )
	{
		"float", "double", "decimal", "int", "long", "short", "byte", "sbyte",
		"uint", "ulong", "ushort", "nint", "nuint",
		"float?", "double?", "decimal?", "int?", "long?", "short?",
	};

	//What a parameter is declared as: the word in front of its name. Nothing about generics or arrays, on purpose. A type this does not read as one of
	//Numeric is a type whose default is left alone, and being wrong in that direction costs a note in the report rather than a file that does not build.
	private static string TypeOf( string parameter )
	{
		var words = parameter.Split( [' ', '\t'], StringSplitOptions.RemoveEmptyEntries );

		return words.Length >= 2 ? words[^2] : "";
	}

	//The parts of a comma separated list, as places in the line.
	private static List<(int Start, int Length)> Split( string code, int at, int length )
	{
		var parts = new List<(int Start, int Length)>( 4 );
		var depth = 0;
		var start = at;
		var end = at + length;

		for ( var i = at; i <= end; i++ )
		{
			if ( i < end )
			{
				var c = code[i];

				if ( c is '<' or '(' or '[' ) { depth++; continue; }
				if ( c is '>' or ')' or ']' ) { depth--; continue; }
				if ( c != ',' || depth != 0 ) continue;
			}

			parts.Add( ( start, i - start ) );
			start = i + 1;
		}

		return parts;
	}

	//Converts an expression handed to the engine term by term, skipping terms already in metres
	private static int Wrap( string[] lines, int i, string code, (int Start, int Length) span,
		string expression, PortUnits units, PortCodeProject project, HashSet<string> metres,
		HashSet<string> arrived, IReadOnlyDictionary<string, string> locals, HashSet<string> doubtful )
	{
		var changed = 0;

		//Right to left again, for the same reason.
		foreach ( var term in Terms( code, span ).OrderByDescending( x => x.Start ) )
		{
			if ( term.Length <= 0 ) continue;

			var start = term.Start;
			var length = term.Length;

			var text = code.Substring( start, length );

			//"name: value" hands the value over under a name; only the value is a number, and the name has to stay outside the multiplication.
			if ( Named.Match( text ) is { Success: true, Length: > 0 } named )
			{
				start += named.Length;
				length -= named.Length;

				if ( length <= 0 ) continue;

				text = code.Substring( start, length );
			}

			//Written to by the callee rather than handed over, so there is no value here yet.
			if ( WrittenTo.IsMatch( text ) ) continue;

			//An array being made rather than a measurement being handed over.
			if ( Allocates.IsMatch( text ) ) continue;

			//More than one expression, so there is no single value to convert.
			if ( Splits( text ) ) continue;

			//Yes or no, rather than how much.
			if ( Boolean.IsMatch( text ) ) continue;

			//A constant of somebody's type, which is how an enum is written.
			if ( Nested.IsMatch( text ) ) continue;

			//Half of an expression, from a call that runs on to the next line.
			if ( !Balanced( text ) ) continue;

			//Nothing to convert, or a conversion already there from a previous run over this project.
			if ( text.Trim().Length == 0 ) continue;
			if ( text.Contains( units.ConversionExpression, StringComparison.Ordinal ) ) continue;
			if ( text.Contains( ".Select( each =>", StringComparison.Ordinal ) ) continue;


			//Already one of this engine's numbers - unless the term is a direction times a length, in
			//which case the length is still the game's and it is the only part of the term to convert.
			if ( Metres( code, term, project, metres, arrived, locals ) )
			{
				if ( Magnitude( code, (start, length), units, project, metres, arrived, locals ) is not { } carries )
					continue;

				start = carries.Start;
				length = carries.Length;
				text = code.Substring( start, length );
			}

			//Or a name the file uses for two different things, which is not worth guessing about in either direction.
			if ( Word.Matches( text ).Any( x => doubtful.Contains( x.Value ) ) ) continue;

			//Or not a number at all.
			if ( project.Unmeasurable( text, locals ) ) continue;
			if ( project.NotANumber( text, locals ) ) continue;

			//From the line rather than the blanked copy, so a string inside the term is written back as it was written.
			var written = lines[i].Substring( start, length ).Trim();

			if ( written.Length == 0 ) continue;

			lines[i] = lines[i][..start] +
				$" {Converted( written, expression )} " +
				lines[i][( start + length )..];

			++changed;
		}

		return changed;
	}

	//What this expression measures, where it is one of this engine's.
	private static PortDimension Measured( string code, Group expression, PortCodeProject project,
		HashSet<string> metres, HashSet<string> arrived, IReadOnlyDictionary<string, string> locals )
		=> Measured( code, ( expression.Index, expression.Length ), project, metres, arrived, locals );

	private static PortDimension Measured( string code, (int Start, int Length) span,
		PortCodeProject project, HashSet<string> metres, HashSet<string> arrived,
		IReadOnlyDictionary<string, string> locals )
	{
		foreach ( Match word in Word.Matches( code.Substring( span.Start, span.Length ) ) )
		{
			var at = span.Start + word.Index;

			if ( project.MetreOf.TryGetValue( word.Value, out var known ) ) return known;

			//A name this file worked out for itself. Length rather than nothing: following an assignment says a name holds one of this
			//engine's numbers, not which kind - and every kind that moves moves by the same amount, so the multiplier is the same either way.
			if ( metres.Contains( word.Value ) || arrived.Contains( word.Value ) )
				return PortDimension.Length;

			if ( !project.EngineRead( code, at, word.Value, locals ) ) continue;

			var dimension = project.DimensionOf( code, at, word.Value, locals );

			if ( dimension is not ( PortDimension.None or PortDimension.Unknown ) ) return dimension;
		}

		return PortDimension.Unknown;
	}

	//Members that measure nothing (directions, rotations); reading one says nothing about units
	private static readonly HashSet<string> Dimensionless = new( StringComparer.Ordinal )
	{
		"Up", "Down", "Left", "Right", "Forward", "Backward",
		"Rotation", "WorldRotation", "LocalRotation",
		"Tangent", "Normal", "Direction",
	};

	//What is being read off a name here, if anything is.
	private static string Read( string code, int after )
	{
		var i = after;

		while ( i < code.Length && code[i] == ' ' ) i++;

		if ( i >= code.Length || code[i] != '.' ) return "";

		i++;

		while ( i < code.Length && code[i] == ' ' ) i++;

		var start = i;

		while ( i < code.Length && ( char.IsLetterOrDigit( code[i] ) || code[i] == '_' ) ) i++;

		return code[start..i];
	}

	//The factors of a term, split where it is multiplied.
	private static List<(int Start, int Length)> Factors( string code, (int Start, int Length) term )
	{
		var parts = new List<(int Start, int Length)>( 3 );

		var depth = 0;
		var start = term.Start;
		var end = term.Start + term.Length;

		for ( var i = term.Start; i < end; i++ )
		{
			var c = code[i];

			if ( c is '(' or '[' ) { depth++; continue; }
			if ( c is ')' or ']' ) { depth--; continue; }

			if ( depth != 0 || c != '*' ) continue;

			parts.Add( (start, i - start) );
			start = i + 1;
		}

		parts.Add( (start, end - start) );

		return parts;
	}

	//Whether this factor is a direction, and so carries no length of its own.
	private static bool Points( string code, (int Start, int Length) factor )
	{
		if ( factor.Length <= 0 ) return false;

		var text = code.Substring( factor.Start, factor.Length );

		return Word.Matches( text ).Any( x => Dimensionless.Contains( x.Value ) );
	}

	//The one factor carrying length in a term that's otherwise a direction; null unless exactly one
	private static (int Start, int Length)? Magnitude( string code, (int Start, int Length) term,
		PortUnits units, PortCodeProject project, HashSet<string> metres, HashSet<string> arrived,
		IReadOnlyDictionary<string, string> locals )
	{
		var factors = Factors( code, term );

		if ( factors.Count < 2 ) return null;
		if ( !factors.Any( x => Points( code, x ) ) ) return null;

		(int Start, int Length)? only = null;

		foreach ( var factor in factors )
		{
			if ( factor.Length <= 0 ) continue;
			if ( Points( code, factor ) ) continue;

			var text = code.Substring( factor.Start, factor.Length );

			if ( text.Trim().Length == 0 ) continue;
			if ( text.Contains( units.ConversionExpression, StringComparison.Ordinal ) ) return null;

			//Already one of this engine's, so it is not the game's length.
			if ( Metres( code, factor, project, metres, arrived, locals ) ) continue;
			if ( project.Unmeasurable( text, locals ) ) continue;
			if ( project.NotANumber( text, locals ) ) continue;

			//Two candidates is a term this cannot read.
			if ( only is not null ) return null;

			only = factor;
		}

		return only;
	}

	private static bool Metres( string code, (int Start, int Length) term, PortCodeProject project,
		HashSet<string> metres, HashSet<string> arrived, IReadOnlyDictionary<string, string> locals )
	{
		var text = code.Substring( term.Start, term.Length );

		foreach ( Match word in Word.Matches( text ) )
		{
			var at = term.Start + word.Index;

			//A rotation or a direction, wherever it stands. Reading one is not evidence about the number beside it, and neither is the
			//type it was taken from: "Rotation.LookAt( a.Tangent, a.Up ).Right" is a unit vector however many names in it hold metres.
			if ( Dimensionless.Contains( word.Value ) ) continue;

			if ( metres.Contains( word.Value ) || arrived.Contains( word.Value )
				//What the project itself worked out - a member holding one of this engine's numbers, or a function of its own that hands one back.
				|| project.Metres.Contains( word.Value ) )
			{
				//Unless what is being taken off it is a direction, which is the one thing a name in metres can hand back that is not.
				if ( !Dimensionless.Contains( Read( code, at + word.Length ) ) )
					return true;

				continue;
			}

			//A call is not a read. "box.Scale" off a collider is one of this engine's numbers and is left alone; "WishVelocity.ClampLength(
			//x )" only borrows the receiver, and what goes into it is still the game's - which is the whole of a player's air control.
			if ( Invoked( code, at + word.Length ) ) continue;

			if ( !project.EngineRead( code, at, word.Value, locals ) ) continue;

			//Dimensionless engine values (rotations, directions) aren't proof of metres; Unknown is left alone
			var dimension = project.MetreOf.TryGetValue( word.Value, out var held )
				? held
				: project.DimensionOf( code, at, word.Value, locals );

			if ( dimension is PortDimension.None ) continue;

			return true;
		}

		return false;
	}

	//Value with its conversion applied; collections (hull point arrays) convert per element
	private static string Converted( string written, string expression )
	{
		foreach ( var call in Collections )
		{
			if ( !written.EndsWith( call, StringComparison.Ordinal ) ) continue;

			var of = written[..^call.Length];

			if ( of.Length == 0 ) break;

			return $"{of}.Select( each => each * {expression} ){call}";
		}

		return $"({written}) * {expression}";
	}

	private static readonly string[] Collections = [".ToArray()", ".ToList()"];

	//Whether a bracket opens straight after this, making it a call.
	private static bool Invoked( string code, int after )
	{
		while ( after < code.Length && code[after] == ' ' ) after++;

		return after < code.Length && code[after] == '(';
	}

	//Names in this file holding engine measurements, learnt from assignments until stable
	internal static HashSet<string> Metres( string[] code, PortCodeProject project,
		IReadOnlyDictionary<string, string> locals, HashSet<string> doubtful )
	{
		var names = new HashSet<string>( StringComparer.Ordinal );

		for ( var round = 0; round < 4; round++ )
		{
			var before = names.Count;

			foreach ( var line in code )
			{
				foreach ( var (name, measured) in Bindings( line, project, names, locals ) )
				{
					if ( measured ) names.Add( name );
				}
			}

			if ( names.Count == before ) break;
		}

		//Names also given non-measurements in the file are doubtful: neither converted nor trusted
		foreach ( var line in code )
		{
			foreach ( var (name, measured) in Bindings( line, project, names, locals ) )
			{
				if ( measured || !names.Contains( name ) ) continue;

				doubtful.Add( name );
			}
		}

		foreach ( var name in doubtful ) names.Remove( name );

		return names;
	}

	//Every name given a value on this line, and whether what it was given is one of this engine's measurements.
	internal static List<(string Name, bool Measured)> Bindings( string code,
		PortCodeProject project, HashSet<string> names, IReadOnlyDictionary<string, string> locals )
	{
		var found = new List<(string Name, bool Measured)>();

		if ( code.Trim().Length == 0 ) return found;

		foreach ( Match bound in Bound.Matches( code ) )
		{
			//A loop counter counts. It is written exactly like anything else and means something else entirely.
			if ( Counter( code, bound.Index ) ) continue;

			var value = bound.Groups["value"];

			//What the name was given, not what that was given: a trace built from two world positions is a trace.
			var head = PortCodeProject.Head( code[..( value.Index + value.Length )] );

			var dimension = Measured( head, ( value.Index, value.Length ),
				project, names, names, locals );

			found.Add( ( bound.Groups["name"].Value,
				dimension is not ( PortDimension.None or PortDimension.Unknown )
					|| PortCodeFlow.Constructs( value.Value ) ) );
		}

		//Game fields assigned without a declaration (_cameraPos = FollowPosition;); engine names excluded
		foreach ( Match given in GivenBare.Matches( code ) )
		{
			var name = given.Groups["member"].Value;

			//A local written out with no value and given one later counts as much as a field: "Vector3
			//desiredPos;" and then a line that puts a place in it is the same thing said in two lines.
			if ( !project.Members.Contains( name ) && !locals.ContainsKey( name ) ) continue;

			var value = given.Groups["value"];
			var head = PortCodeProject.Head( code[..( value.Index + value.Length )] );

			var dimension = Measured( head, ( value.Index, value.Length ),
				project, names, names, locals );

			found.Add( ( name, dimension is not ( PortDimension.None or PortDimension.Unknown )
				|| PortCodeFlow.Constructs( value.Value ) ) );
		}

		return found;
	}

	//Whether this binding is the counter of a for statement.
	private static bool Counter( string code, int at )
	{
		var before = code[..at].TrimEnd();

		if ( !before.EndsWith( "(", StringComparison.Ordinal ) ) return false;

		return before[..^1].TrimEnd().EndsWith( "for", StringComparison.Ordinal );
	}

	//The terms an expression is summed from, as places in the line. Only the pluses and minuses this expression owns - what is inside
	//a bracket belongs to whatever that bracket is. A sign with nothing before it is part of the number rather than a term of its own.
	internal static List<(int Start, int Length)> Terms( string code, (int Start, int Length) span )
	{
		var terms = new List<(int Start, int Length)>( 4 );

		var depth = 0;
		var start = span.Start;
		var end = span.Start + span.Length;

		//A choice is one value written two ways, not a sum. Split it and the multiplication
		//lands between the question mark and the colon, and what comes out is not C# at all.
		for ( var i = span.Start; i < end; i++ )
		{
			if ( code[i] is '(' or '[' ) depth++;
			else if ( code[i] is ')' or ']' ) depth--;
			else if ( depth == 0 && code[i] == '?' && ( i + 1 >= end || code[i + 1] is not ('.' or '?' or '[') ) )
			{
				terms.Add( span );
				return terms;
			}
		}

		depth = 0;

		for ( var i = span.Start; i < end; i++ )
		{
			var c = code[i];

			if ( c is '(' or '[' ) depth++;
			else if ( c is ')' or ']' ) depth--;
			else if ( depth == 0 && c is '+' or '-' && Binary( code, i, span.Start ) )
			{
				terms.Add( ( start, i - start ) );
				start = i + 1;
			}
		}

		terms.Add( ( start, end - start ) );

		return terms;
	}

	//Whether the sign here joins two terms rather than opening one.
	private static bool Binary( string code, int at, int from )
	{
		for ( var i = at - 1; i >= from; i-- )
		{
			if ( code[i] == ' ' ) continue;

			return char.IsLetterOrDigit( code[i] ) || code[i] is ')' or ']' or '_';
		}

		return false;
	}
}
