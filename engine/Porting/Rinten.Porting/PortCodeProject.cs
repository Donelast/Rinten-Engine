using System.Text.RegularExpressions;

namespace Rinten.Porting;

/// <summary>What a port has to know about a whole project before it can convert one line of it.</summary>
public sealed class PortCodeProject
{
	/// <summary>Types the project declares.</summary>
	public HashSet<string> Types { get; } = new( StringComparer.Ordinal );

	/// <summary>Functions the project declares itself, by name.</summary>
	public HashSet<string> Methods { get; } = new( StringComparer.Ordinal );

	/// <summary>Members the project declares, by name.</summary>
	public HashSet<string> Members { get; } = new( StringComparer.Ordinal );

	/// <summary>Declared type of each member, so receivers like CharacterController.Velocity can be resolved</summary>
	public Dictionary<string, string> MemberTypes { get; } = new( StringComparer.Ordinal );

	/// <summary>Members that hold one of this engine's measurements, so are already in metres and must never be converted.</summary>
	public HashSet<string> Metres { get; } = new( StringComparer.Ordinal );

	/// <summary>What each of those measures, for a site that has to say it.</summary>
	public Dictionary<string, PortDimension> MetreOf { get; } = new( StringComparer.Ordinal );

	/// <summary>The project's own functions that are handed a measurement, and which of their parameters is one.</summary>
	public Dictionary<string, Dictionary<int, PortDimension>> Needs { get; } =
		new( StringComparer.Ordinal );

	/// <summary>Project names that the engine also measures (e.g. Position); unresolved ones assume the project's</summary>
	public HashSet<string> Shared { get; } = new( StringComparer.Ordinal );

	private static readonly Regex TypeDeclared = new(
		@"^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|new|unsafe|readonly|file)\s+)*" +
		@"(?:class|struct|record|interface|enum)\s+(?<name>[A-Za-z_]\w*)",
		RegexOptions.Compiled );

	//Not inside an attribute, so [Group( "Flight" )] isn't misread as a method declaration
	private const string NotAnAttribute = @"(?!\[)";

	private const string Modifiers =
		@"(?:(?:public|private|protected|internal|static|readonly|const|virtual|override|" +
		@"new|sealed|abstract|partial|required|volatile|extern|unsafe|async)\s+)+";

	//A member being declared, with whatever it is given.
	private static readonly Regex MemberDeclared = new(
		@"^\s*(?:\[[^\]]*\]\s*)*" + NotAnAttribute + Modifiers +
		@"(?<type>[\w<>\[\],\.\?]+)\s+(?<name>[A-Za-z_]\w*)\s*" +
		@"(?:(?<arrow>=>)\s*(?<value>[^;]+);|\{[^{}]*\}\s*=\s*(?<init>[^;]+);|=\s*(?<assigned>[^;]+);|\{|;)",
		RegexOptions.Compiled );

	//Member with attributes but no access modifier: [RequireComponent] NavMeshAgent agent { get; set; }
	private static readonly Regex MemberPlain = new(
		@"^\s*(?:\[[^\]]*\]\s*)+(?<type>[\w<>\[\],\.\?]+)\s+(?<name>[A-Za-z_]\w*)\s*" +
		@"(?:(?<arrow>=>)\s*(?<value>[^;]+);|\{[^{}]*\}\s*=\s*(?<init>[^;]+);|=\s*(?<assigned>[^;]+);|\{|;)",
		RegexOptions.Compiled );

	//Private field with no modifiers; only matched in a class body since it looks like a local
	private static readonly Regex FieldPlain = new(
		@"^\s*(?<type>[A-Za-z_][\w<>\[\],\.\?]*)\s+(?<name>[A-Za-z_]\w*)\s*" +
		@"(?:=\s*(?<assigned>[^;]+);|;)",
		RegexOptions.Compiled );

	//A function being declared, with its parameter list.
	private static readonly Regex MethodDeclared = new(
		@"^\s*(?:\[[^\]]*\]\s*)*" + NotAnAttribute + Modifiers.Replace( ")+", ")*" ) +
		@"(?!(?:new|return|if|while|for|foreach|using|else|await|throw|switch|lock|yield)\b)" +
		@"(?<returns>[\w<>\[\],\.\?]+)\s+(?<name>[A-Za-z_]\w*)\s*(?=\()",
		RegexOptions.Compiled );

	//Property declaration (no parameter list), so what its getter returns can be learnt
	private static readonly Regex PropertyDeclared = new(
		@"^\s*(?:\[[^\]]*\]\s*)*" + NotAnAttribute + Modifiers.Replace( ")+", ")*" ) +
		@"(?!(?:new|return|if|while|for|foreach|using|else|await|throw|switch|lock|yield|" +
		@"class|struct|record|interface|enum|namespace|get|set|case|default)\b)" +
		@"(?<returns>[\w<>\[\],\.\?]+)\s+(?<name>[A-Za-z_]\w*)\s*(?=$|\{|=>)",
		RegexOptions.Compiled );

	//A value being handed back out of a function.
	private static readonly Regex Returned = new(
		@"(?:^|[^\w.])return\s+(?<value>[^;]+);",
		RegexOptions.Compiled );

	//A local written out with its type.
	private static readonly Regex LocalTyped = new(
		@"(?:^|[^\w.])(?<type>[A-Z][\w\.]*(?:<[^<>()]*>)?(?:\[\])?)\s+(?<name>[a-z_]\w*)\s*(?:=[^=]|\)|,|;)",
		RegexOptions.Compiled );

	//Local or parameter with a keyword type (int, float...), so e.g. an int isn't converted as a length
	private static readonly Regex LocalPrimitive = new(
		@"(?:^|[^\w.])(?<type>(?:bool|byte|sbyte|char|short|ushort|int|uint|long|ulong|nint|nuint|" +
		@"float|double|decimal|string)(?:\?|\[[,\s]*\])*)\s+(?<name>[a-z_]\w*)\s*(?:=[^=]|\)|,|;|\{)",
		RegexOptions.Compiled );

	//A local whose type is written as the thing being asked for rather than as the local: TryGet<BoxCollider>( out var box ) .
	private static readonly Regex LocalAsked = new(
		@"<\s*(?<type>[A-Za-z_][\w\.]*)\s*>\s*\(\s*out\s+var\s+(?<name>[A-Za-z_]\w*)",
		RegexOptions.Compiled );

	//The same, where what was asked for is handed back instead.
	private static readonly Regex LocalGot = new(
		@"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*[\w\.]*<\s*(?<type>[A-Za-z_][\w\.]*)\s*>",
		RegexOptions.Compiled );

	//A local given something new.
	private static readonly Regex LocalNew = new(
		@"\bvar\s+(?<name>[A-Za-z_]\w*)\s*=\s*new\s+(?<type>[A-Za-z_]\w*)",
		RegexOptions.Compiled );

	private static readonly Regex Word = new( @"[A-Za-z_]\w*", RegexOptions.Compiled );

	/// <summary>Reads a project and works out what it measures where.</summary>
	public static PortCodeProject Read( IReadOnlyList<(string Named, string[] Code)> files )
	{
		var project = new PortCodeProject();

		//What the project owns. Everything else a file mentions is this engine's.
		foreach ( var (_, code) in files )
		{
			//How deep in braces the line is, and how deep the class body of the moment is - which is the
			//only thing that tells a field written without a modifier from a local written the same way.
			var depth = 0;
			var inside = -1;

			foreach ( var line in code )
			{
				var was = depth;

				depth += line.Count( x => x == '{' ) - line.Count( x => x == '}' );

				var type = TypeDeclared.Match( line );

				if ( type.Success )
				{
					project.Types.Add( type.Groups["name"].Value );

					//The body, which is this line's brace where it has one and the next line's where the game writes it underneath.
					inside = line.Contains( '{' ) ? depth : depth + 1;
				}

				var declared = MethodDeclared.Match( line );

				if ( declared.Success )
				{
					project.Methods.Add( declared.Groups["name"].Value );
					continue;
				}

				var member = MemberDeclared.Match( line );

				if ( !member.Success ) member = MemberPlain.Match( line );

				//A field with no modifier and no attribute in front of it, which is how a game writes the state it keeps to itself: "Vector3
				//_cameraPos;". Only in the body of a class - written the same way inside a method it is a local, and locals are followed somewhere else.
				if ( !member.Success && inside >= 0 && was == inside ) member = FieldPlain.Match( line );

				if ( !member.Success ) continue;

				project.Members.Add( member.Groups["name"].Value );
				project.MemberTypes[member.Groups["name"].Value] = member.Groups["type"].Value;
			}
		}

		foreach ( var name in project.Members )
		{
			if ( Table( name ) is not ( PortDimension.None or PortDimension.Unknown ) )
				project.Shared.Add( name );
		}

		//Then what holds metres, and which of the project's functions are handed them.
		for ( var round = 0; round < 8; round++ )
		{
			var before = project.Size();

			foreach ( var (_, code) in files )
			{
				var locals = Locals( code );

				project.LearnMetres( code, locals );
				project.LearnNeeds( code, locals );
			}

			if ( project.Size() == before ) break;
		}

		return project;
	}

	private int Size() => Metres.Count + Needs.Sum( x => x.Value.Count );

	/// <summary>The names of a parameter list, in order.</summary>
	public static string[] ParameterNames( string list )
		=> [.. Parameters( list ).Select( x => x.Name )];

	/// <summary>The names and types of a parameter list, in order.</summary>
	public static List<(string Name, string Type)> Parameters( string list )
	{
		if ( list.Trim().Length == 0 ) return [];

		var names = new List<(string Name, string Type)>( 4 );
		var depth = 0;
		var start = 0;

		for ( var i = 0; i <= list.Length; i++ )
		{
			if ( i < list.Length )
			{
				var c = list[i];

				if ( c is '<' or '(' or '[' ) { depth++; continue; }
				if ( c is '>' or ')' or ']' ) { depth--; continue; }
				if ( c != ',' || depth != 0 ) continue;
			}

			var part = list[start..i];
			start = i + 1;

			//The name is the last word before a default value, where there is one: "float radius = 2.0f" is called radius.
			var head = part.Split( '=' )[0].Trim();
			var words = Word.Matches( head ).Select( x => x.Value ).ToList();

			names.Add( words.Count > 0
				? ( words[^1], words.Count > 1 ? words[^2] : "" )
				: ( "", "" ) );
		}

		return names;
	}

	//Members whose value comes from this engine, so hold metres whatever the game calls them.
	private void LearnMetres( string[] code, IReadOnlyDictionary<string, string> locals )
	{
		foreach ( var line in code )
		{
			if ( MethodDeclared.IsMatch( line ) ) continue;

			var member = MemberDeclared.Match( line );

			if ( !member.Success ) member = MemberPlain.Match( line );
			if ( !member.Success ) continue;

			var value = member.Groups["value"].Success ? member.Groups["value"].Value
				: member.Groups["init"].Success ? member.Groups["init"].Value
				: member.Groups["assigned"].Success ? member.Groups["assigned"].Value
				: null;

			if ( value is null ) continue;

			//Only something that could hold a measurement. A bool named from a speed - "IsRunning => Velocity.Length >= 200" - mentions one without being one.
			if ( !Measurable( member.Groups["type"].Value ) ) continue;

			//A shape built out of this engine's constructors comes out in metres, whatever the game's own numbers went into it.
			if ( PortCodeFlow.Constructs( value ) )
			{
				Learnt( member.Groups["name"].Value, PortDimension.Length );
				continue;
			}

			var dimension = Measures( value, locals );

			if ( dimension is not ( PortDimension.None or PortDimension.Unknown ) )
				Learnt( member.Groups["name"].Value, dimension );
		}

		LearnGiven( code, locals );
		LearnSums( code, locals );
	}

	//A member being given something, anywhere rather than where it was declared.
	private static readonly Regex GivenAnywhere = new(
		@"(?:^|[^\w.])(?<name>[A-Za-z_]\w*)\s*(?<op>=(?!=)|\+=|-=)\s*(?<value>[^;]+);",
		RegexOptions.Compiled );

	//Members assigned an engine measurement in code (Velocity = mover.Velocity) hold metres from then on
	private void LearnGiven( string[] code, IReadOnlyDictionary<string, string> locals )
	{
		foreach ( var line in code )
		{
			if ( MethodDeclared.IsMatch( line ) ) continue;

			foreach ( Match given in GivenAnywhere.Matches( line ) )
			{
				var name = given.Groups["name"].Value;

				if ( !Members.Contains( name ) || Metres.Contains( name ) ) continue;
				if ( !MemberTypes.TryGetValue( name, out var type ) || !Measurable( type ) ) continue;

				var dimension = Measures( given.Groups["value"].Value, locals );

				if ( dimension is not ( PortDimension.None or PortDimension.Unknown ) )
					Learnt( name, dimension );
			}
		}
	}

	//A plain name added to an engine measurement is one too (Head.WorldPosition + EyeOffset)
	private void LearnSums( string[] code, IReadOnlyDictionary<string, string> locals )
	{
		foreach ( var line in code )
		{
			if ( line.Trim().Length == 0 ) continue;

			var terms = PortCodeUnits.Terms( line, ( 0, line.Length ) );

			if ( terms.Count < 2 ) continue;

			var dimension = PortDimension.Unknown;

			foreach ( var term in terms )
			{
				if ( term.Length <= 0 ) continue;

				var found = Measures( line.Substring( term.Start, term.Length ), locals );

				if ( found is not ( PortDimension.None or PortDimension.Unknown ) )
				{
					dimension = found;
					break;
				}
			}

			if ( dimension is PortDimension.None or PortDimension.Unknown ) continue;

			foreach ( var term in terms )
			{
				if ( term.Length <= 0 ) continue;

				var written = line.Substring( term.Start, term.Length ).Trim().TrimEnd( ';', ')' ).Trim();
				var name = Word.Match( written );

				if ( !name.Success || name.Length != written.Length ) continue;
				if ( Metres.Contains( written ) ) continue;

				//Only something the project declares. A local called "d" holding a distance in one file says nothing about the "d" three files away that holds a dot
				//product - and this table is read by the whole project. Locals are worked out per file, where the file can be checked for disagreeing with itself.
				if ( !Members.Contains( written ) ) continue;
				if ( !MemberTypes.TryGetValue( written, out var type ) || !Measurable( type ) ) continue;

				Learnt( written, dimension );
			}
		}
	}

	/// <summary>Whether an expression is one of this engine's measurements - directly, or through a name already known to hold one.</summary>
	public PortDimension Measures( string expression )
		=> Measures( expression, NoLocals );

	private static readonly Dictionary<string, string> NoLocals = new( StringComparer.Ordinal );

	public PortDimension Measures( string expression, IReadOnlyDictionary<string, string> locals )
		=> Measures( expression, locals, null );

	public PortDimension Measures( string expression, IReadOnlyDictionary<string, string> locals,
		HashSet<string> also )
	{
		expression = Head( expression );

		foreach ( Match word in Word.Matches( expression ) )
		{
			var name = word.Value;

			if ( MetreOf.TryGetValue( name, out var known ) ) return known;
			if ( also is not null && also.Contains( name ) ) return PortDimension.Length;

			if ( !EngineRead( expression, word.Index, name, locals ) ) continue;

			var dimension = SceneUnits.Dimension( "", name );

			if ( dimension is not ( PortDimension.None or PortDimension.Unknown ) ) return dimension;
		}

		return PortDimension.Unknown;
	}

	/// <summary>Whether an expression is one of this engine's measurements.</summary>
	public bool Holds( string expression )
		=> Measures( expression ) is not ( PortDimension.None or PortDimension.Unknown );

	//Numeric types an engine measurement can be stored in; keeps bools from being learnt as lengths
	private static readonly HashSet<string> Numeric = new( StringComparer.Ordinal )
	{
		"float", "double", "decimal", "Single", "Double",
		"Vector2", "Vector3", "Vector4", "BBox", "Transform",
	};

	private static bool Measurable( string type )
		=> Numeric.Contains( type.TrimEnd( '?' ) );

	//Engine value types whose contents belong to the declarer (a game's Vector3 holds game numbers)
	private static readonly HashSet<string> Held = new( StringComparer.Ordinal )
	{
		"Vector2", "Vector3", "Vector4", "Rotation", "Angles", "Transform", "BBox",
	};

	private void Learnt( string name, PortDimension dimension )
	{
		Metres.Add( name );

		if ( dimension is not ( PortDimension.None or PortDimension.Unknown ) )
			MetreOf[name] = dimension;
		else if ( !MetreOf.ContainsKey( name ) )
			MetreOf[name] = PortDimension.Length;
	}

	//The names inside one file that were given one of this engine's measurements - var
	//speed = Velocity.Length - which the rewrite pass works out the same way for itself.
	private HashSet<string> LocalMetres( string[] code, IReadOnlyDictionary<string, string> locals )
	{
		var names = new HashSet<string>( StringComparer.Ordinal );

		for ( var round = 0; round < 4; round++ )
		{
			var before = names.Count;

			foreach ( var line in code )
			{
				foreach ( var (name, measured) in PortCodeUnits.Bindings( line, this, names, locals ) )
				{
					if ( measured ) names.Add( name );
				}
			}

			if ( names.Count == before ) break;
		}

		//Same rule as the rewrite pass: a name the file gives two different kinds of value is not one of them.
		foreach ( var line in code )
		{
			foreach ( var (name, measured) in PortCodeUnits.Bindings( line, this, names, locals ) )
			{
				if ( !measured ) names.Remove( name );
			}
		}

		return names;
	}

	//Project functions whose parameters reach the engine; their callers must convert to metres
	private void LearnNeeds( string[] code, IReadOnlyDictionary<string, string> locals )
	{
		var method = "";
		var parameters = new List<(string Name, string Type)>();
		var held = LocalMetres( code, locals );

		foreach ( var line in code )
		{
			var declared = MethodDeclared.Match( line );

			var rest = line;

			if ( declared.Success )
			{
				var list = PortCodeUnits.ListAt( line, declared.Index + declared.Length );

				method = declared.Groups["name"].Value;
				parameters = list.Start < 0 ? [] : Parameters( line.Substring( list.Start, list.Length ) );

				//Everything after the brackets, because a method written as one expression has
				//its body on this line - and that body is where its parameters are handed over.
				var after = list.Start < 0 ? line.Length : list.Start + list.Length + 1;

				if ( after >= line.Length ) continue;

				rest = new string( ' ', after ) + line[after..];

				//"Vector3 Where() => WorldPosition + offset;" hands back what is after the arrow, which no return statement will ever say.
				var arrow = rest.IndexOf( "=>", StringComparison.Ordinal );

				if ( arrow >= 0 && Measurable( declared.Groups["returns"].Value ) )
					Hands( declared.Groups["name"].Value, rest[( arrow + 2 )..].TrimEnd( ';', ' ' ), locals, held );
			}
			else if ( PropertyDeclared.Match( line ) is { Success: true } property
				&& Measurable( property.Groups["returns"].Value ) )
			{
				//A property is a function with no arguments, and its getter is as much of a boundary as any method's body.
				method = property.Groups["name"].Value;
				parameters = [];

				var after = property.Index + property.Length;

				rest = after >= line.Length ? line : new string( ' ', after ) + line[after..];

				var arrow = rest.IndexOf( "=>", StringComparison.Ordinal );

				if ( arrow >= 0 )
					Hands( method, rest[( arrow + 2 )..].TrimEnd( ';', ' ' ), locals, held );
			}

			//A function with no parameters still hands something back, and what it hands back is half of what this pass is for.
			if ( method.Length == 0 ) continue;

			foreach ( var argument in PortCodeFlow.MeasuredArguments( rest ) )
			{
				if ( argument.Length <= 0 ) continue;
				if ( !EngineCall( rest, argument.At, argument.Call, locals ) ) continue;

				Mark( method, parameters,
					rest.Substring( argument.Start, argument.Length ), argument.Dimension );
			}

			//What the function hands back. A game's own helper that works out where something should stand returns a place in
			//this engine's metres, and converting what came out of it sends whatever asked forty times too close to the origin.
			foreach ( Match returned in Returned.Matches( rest ) )
			{
				if ( method.Length == 0 ) continue;

				Hands( method, returned.Groups["value"].Value, locals, held );
			}

			//A body written as one expression on the line after its declaration, which is how a game writes its small helpers.
			var trimmed = rest.TrimStart();

			if ( method.Length > 0 && trimmed.StartsWith( "=>", StringComparison.Ordinal ) )
				Hands( method, trimmed[2..].TrimEnd( ';', ' ' ), locals, held );

			//A parameter held up against one of this engine's measurements has to arrive in the same units to mean anything. Which makes
			//its default the caller's business too - and a friction threshold left in the game's units is a player who never slows down.
			foreach ( Match compared in PortCodeUnits.ComparedName.Matches( rest ) )
			{
				var left = compared.Groups["left"].Value;

				//Skip comparisons against the method's own parameters; that's what this pass is working out
				if ( parameters.Any( x => x.Name == left ) ) continue;

				var dimension = Measures( left, locals, held );

				if ( dimension is PortDimension.None or PortDimension.Unknown ) continue;

				Mark( method, parameters, compared.Groups["value"].Value, dimension );
			}

			//And through the project's own functions, so a measurement handed on twice over is still the outermost caller's to convert.
			foreach ( var (name, needs) in Needs.ToList() )
			{
				var spans = PortCodeFlow.ArgumentsOf( rest, name );

				if ( spans.Count == 0 ) continue;

				foreach ( var (index, dimension) in needs.ToList() )
				{
					if ( index >= spans.Count || spans[index].Length <= 0 ) continue;

					Mark( method, parameters,
						rest.Substring( spans[index].Start, spans[index].Length ), dimension );
				}
			}
		}
	}

	//What a function hands back, where that is one of this engine's measurements. A shape counts as much as a number does. Bounds() builds its box out
	//of the game's own vertices, so the box that comes out is already this engine's - and handing it to a mesh after that must not convert it again.
	private void Hands( string method, string value, IReadOnlyDictionary<string, string> locals,
		HashSet<string> held )
	{
		if ( PortCodeFlow.Constructs( value ) )
		{
			Learnt( method, PortDimension.Length );
			return;
		}

		var handed = Measures( value, locals, held );

		if ( handed is ( PortDimension.None or PortDimension.Unknown ) ) return;

		Learnt( method, handed );
		Beside( value, handed );
	}

	//A one-name game member added to an engine measurement is one too; compound terms convert instead
	private void Beside( string value, PortDimension dimension )
	{
		foreach ( var term in PortCodeUnits.Terms( value, ( 0, value.Length ) ) )
		{
			if ( term.Length <= 0 || term.Length == value.Length ) continue;

			var name = value.Substring( term.Start, term.Length ).Trim();

			if ( !Members.Contains( name ) || Metres.Contains( name ) ) continue;
			if ( MemberTypes.TryGetValue( name, out var type ) && !Measurable( type ) ) continue;

			Learnt( name, dimension );
		}
	}

	private void Mark( string method, List<(string Name, string Type)> parameters,
		string argument, PortDimension dimension )
	{
		//Every part of what was handed over, not only the whole of it. "Trace.Ray( around + Vector3.Up * 48f, ... )" says that around is a place as plainly
		//as handing it over on its own would - it is added to one. Without this the conversion lands inside the function, on a value that arrived correct.
		foreach ( var term in PortCodeUnits.Terms( argument, ( 0, argument.Length ) ) )
		{
			if ( term.Length <= 0 || term.Length == argument.Length ) continue;

			Mark( method, parameters, argument.Substring( term.Start, term.Length ), dimension );
		}

		var written = argument.Trim();

		for ( var i = 0; i < parameters.Count; i++ )
		{
			if ( parameters[i].Name.Length == 0 || written != parameters[i].Name ) continue;

			//An index is not a measurement, whatever it is handed to. A mesh generator's Midpoint( int a, int b ) takes two vertex
			//numbers and looks their positions up - and a port that converted those numbers asked for vertex nought point nought two.
			if ( parameters[i].Type.Length > 0 && !Measurable( parameters[i].Type ) ) continue;

			if ( !Needs.TryGetValue( method, out var needs ) )
				Needs[method] = needs = new Dictionary<int, PortDimension>();

			needs[i] = dimension;
		}
	}

	/// <summary>Whether a member read here is this engine's measurement or one of the game's own numbers.</summary>
	public bool EngineRead( string code, int at, string member, IReadOnlyDictionary<string, string> locals )
	{
		if ( Metres.Contains( member ) ) return true;

		var receiver = Receiver( code, at );
		var type = TypeOf( receiver, locals );

		//A component of a game-held Vector3 (pos.z) is a game number, unless the receiver holds metres
		if ( type is not null && Held.Contains( type.TrimEnd( '?' ) )
			&& !Metres.Contains( receiver ) )
		{
			return false;
		}

		//Where the receiver's type is written down somewhere, it settles it - and settles it for members no table of ours lists
		//as well. "box.Scale" on a BoxCollider is a size this engine keeps in metres, and nothing about the word Scale says so.
		if ( type is not null ) return !Types.Contains( type );

		if ( !Members.Contains( member ) )
			return Table( member ) is not ( PortDimension.None or PortDimension.Unknown );

		//A member the project declares, on a receiver nobody wrote a type for.
		if ( receiver.Length == 0 ) return false;

		//A capitalised receiver the project does not declare is one of this engine's types.
		return char.IsUpper( receiver[0] ) && !Members.Contains( receiver );
	}

	/// <summary>Whether a call is the engine's or a same-named game function; receiver type decides, else game's</summary>
	public bool EngineCall( string code, int at, string name, IReadOnlyDictionary<string, string> locals )
	{
		if ( !Methods.Contains( name ) ) return true;

		var type = TypeOf( Receiver( code, at ), locals );

		return type is not null && !Types.Contains( type );
	}

	/// <summary>What a member measures, asking the table with the receiver's type (MaxSpeed has no wildcard)</summary>
	public PortDimension DimensionOf( string code, int at, string member,
		IReadOnlyDictionary<string, string> locals )
	{
		var type = TypeOf( Receiver( code, at ), locals );

		if ( type is not null && !Types.Contains( type ) && char.IsUpper( member[0] ) )
		{
			var exact = SceneUnits.Dimension( type, member );

			if ( exact is not PortDimension.Unknown ) return exact;

			//Written without the namespace, which is how a game says it.
			exact = SceneUnits.Dimension( $"Rinten.{type}", member );

			if ( exact is not PortDimension.Unknown ) return exact;
		}

		return Table( member );
	}

	//Units table lookup for a name in code; only capitalised names, so locals like bounds don't match
	private static PortDimension Table( string name )
	{
		if ( name.Length == 0 || !char.IsUpper( name[0] ) ) return PortDimension.Unknown;

		return SceneUnits.Dimension( "", name );
	}

	//What the project wrote down about a name's type, where it did.
	private string TypeOf( string name, IReadOnlyDictionary<string, string> locals )
	{
		if ( name.Length == 0 ) return null;
		if ( Types.Contains( name ) ) return name;
		if ( locals.TryGetValue( name, out var local ) ) return local;
		if ( MemberTypes.TryGetValue( name, out var member ) ) return member;

		return null;
	}

	/// <summary>Whether this is a name of a type that can't hold a measurement (e.g. a Ray), so never converted</summary>
	public bool Unmeasurable( string expression, IReadOnlyDictionary<string, string> locals )
	{
		var name = expression.Trim();
		var word = Word.Match( name );

		if ( !word.Success || word.Length != name.Length ) return false;

		var type = TypeOf( name, locals );

		return type is not null && !Measurable( type );
	}

	//Expression with call arguments blanked, so Vector3.Dot( a.WorldPosition, ... ) isn't a position
	internal static string Head( string expression )
	{
		var chars = expression.ToCharArray();

		for ( var i = 0; i < chars.Length; i++ )
		{
			if ( chars[i] != '(' ) continue;

			//Only a bracket that belongs to a name. One standing on its own is grouping, and what is inside it is part of this expression.
			var before = i - 1;

			while ( before >= 0 && chars[before] == ' ' ) before--;
			if ( before < 0 || !( char.IsLetterOrDigit( chars[before] ) || chars[before] == '_' ) ) continue;

			var depth = 0;

			for ( var j = i; j < chars.Length; j++ )
			{
				if ( chars[j] == '(' ) depth++;
				else if ( chars[j] == ')' ) depth--;

				if ( j > i && j < chars.Length ) chars[j] = ' ';
				if ( depth == 0 ) { i = j; break; }
			}
		}

		return new string( chars );
	}

	/// <summary>Whether this is plainly not a number, whatever it was handed to.</summary>
	public bool NotANumber( string expression, IReadOnlyDictionary<string, string> locals )
	{
		var name = expression.Trim();
		var word = Word.Match( name );

		if ( !word.Success || word.Length != name.Length ) return false;

		if ( Keywords.Contains( name ) ) return true;
		if ( !char.IsUpper( name[0] ) ) return false;

		return !Members.Contains( name ) && !Metres.Contains( name ) && !locals.ContainsKey( name );
	}

	private static readonly HashSet<string> Keywords = new( StringComparer.Ordinal )
	{
		"null", "true", "false", "default", "this", "base", "value", "var", "new",
	};

	//The name immediately left of the dot at this position.
	private static string Receiver( string code, int at )
	{
		var i = at - 1;

		while ( i >= 0 && code[i] == ' ' ) i--;
		if ( i < 0 || code[i] != '.' ) return "";

		i--;
		while ( i >= 0 && code[i] == ' ' ) i--;

		var end = i + 1;

		while ( i >= 0 && ( char.IsLetterOrDigit( code[i] ) || code[i] == '_' ) ) i--;

		return end > i + 1 ? code[( i + 1 )..end] : "";
	}

	/// <summary>The locals of a file that were written with their type.</summary>
	public static Dictionary<string, string> Locals( string[] code )
	{
		var locals = new Dictionary<string, string>( StringComparer.Ordinal );

		foreach ( var line in code )
		{
			foreach ( Match match in LocalTyped.Matches( line ) )
				locals[match.Groups["name"].Value] = match.Groups["type"].Value;

			foreach ( Match match in LocalPrimitive.Matches( line ) )
				locals[match.Groups["name"].Value] = match.Groups["type"].Value;

			foreach ( Match match in LocalNew.Matches( line ) )
				locals[match.Groups["name"].Value] = match.Groups["type"].Value;

			foreach ( Match match in LocalAsked.Matches( line ) )
				locals[match.Groups["name"].Value] = match.Groups["type"].Value;

			foreach ( Match match in LocalGot.Matches( line ) )
				locals[match.Groups["name"].Value] = match.Groups["type"].Value;
		}

		return locals;
	}
}
