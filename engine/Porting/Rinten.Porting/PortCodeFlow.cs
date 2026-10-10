using System.Linq;
using System.Text.RegularExpressions;

namespace Rinten.Porting;

//Infers what numbers in code measure from the engine API they're handed to, not their names
//Per file only; vocabulary in PortCodeUnits can only veto. Cross-file game fields are reported
internal static class PortCodeFlow
{
	//Engine calls by method name -> dimension per argument; receiver unchecked, ambiguous names excluded
	private static readonly Dictionary<string, PortDimension[]> Calls = new( StringComparer.Ordinal )
	{
		//Character movement.
		["Punch"] = [PortDimension.Speed],
		["Accelerate"] = [PortDimension.Speed],
		["ApplyFriction"] = [PortDimension.None, PortDimension.Speed],
		["MoveTo"] = [PortDimension.Length, PortDimension.None],
		["TryMove"] = [PortDimension.None],
		["TryMoveWithStep"] = [PortDimension.None, PortDimension.Length],
		["TraceDirection"] = [PortDimension.Length],

		//Tracing the world.
		["Ray"] = [PortDimension.Length, PortDimension.Length],
		["FromTo"] = [PortDimension.Length, PortDimension.Length],
		["Sweep"] = [PortDimension.Length, PortDimension.Length],
		["Box"] = [PortDimension.Length, PortDimension.Length, PortDimension.Length],
		["Sphere"] = [PortDimension.Length, PortDimension.Length, PortDimension.Length],
		["Capsule"] = [PortDimension.Length, PortDimension.Length, PortDimension.Length],
		["FindInPhysics"] = [PortDimension.Length, PortDimension.Length],
		["FindBodiesInPhysics"] = [PortDimension.Length, PortDimension.Length],

		//How fat the trace is. A ray given a size sweeps a sphere of it, and a game asking for
		//two inches of clearance would otherwise sweep two metres of it and stop on everything.
		["Size"] = [PortDimension.Length],
		["Radius"] = [PortDimension.Length],

		//What a velocity is eased toward. The second argument is a rate - a share of the way there per second - and moves for nobody.
		["WithAcceleration"] = [PortDimension.Speed, PortDimension.None],

		//The mover this engine's character controller is built out of, which a ported game usually has a copy of the controller
		//around. Everything inside that copy is the game's own - it is the game's code - and this is the line where it stops being.
		["CharacterControllerHelper"] = [PortDimension.None, PortDimension.Length, PortDimension.Speed],

		//Physics bodies.
		["ApplyImpulse"] = [PortDimension.Speed],
		["ApplyImpulseAt"] = [PortDimension.Length, PortDimension.Speed],
		["ApplyForce"] = [PortDimension.Force],
		["ApplyForceAt"] = [PortDimension.Length, PortDimension.Force],

		//Placing things. LookAt takes two directions; FromPositionAndSize omitted like box construction
		["LookAt"] = [PortDimension.None, PortDimension.None],

		//Boxes are often game-internal volumes, so they convert where handed over, not where built
		["BBox"] = [PortDimension.Length],
		["LineBBox"] = [PortDimension.Length],
		["SolidBox"] = [PortDimension.Length],

		//Sphere and Capsule are already above: the tracing overloads measure their arguments the same way the constructors do.

		//Vertex: where a game-built mesh enters the world; only the position is a length
		["Vertex"] = [PortDimension.Length, PortDimension.None, PortDimension.None, PortDimension.None],

		//The other half of a mesh a game builds: the hull it is collided against. Handed over as a whole
		//array rather than a point at a time, which is why it needs its own answer in PortCodeUnits.
		["AddCollisionMesh"] = [PortDimension.Length, PortDimension.None],
		["Transform"] = [PortDimension.Length, PortDimension.None, PortDimension.None],
		["FromHeightAndRadius"] = [PortDimension.Length, PortDimension.Length],
	};

	//Methods whose first argument takes the receiver's dimension; LerpTo's second arg is a fraction
	private static readonly HashSet<string> Carries = new( StringComparer.Ordinal )
	{
		"ClampLength", "LerpTo", "Distance", "DistanceSquared", "AlmostEqual",
		"WithX", "WithY", "WithZ", "SnapToGrid", "ComponentMin", "ComponentMax",
		"Approach", "MoveTowards", "Clamp",
	};

	//Words that are never a name being measured: types, statics, and the keywords that turn up inside an argument.
	private static readonly HashSet<string> NotNames = new( StringComparer.Ordinal )
	{
		"Vector2", "Vector3", "Vector4", "Rotation", "Angles", "Transform", "BBox",
		"Color", "Math", "MathF", "MathX", "Time", "RealTime", "Input", "Scene",
		"Game", "Random", "Screen", "Gizmo", "Sound", "Log", "GameObject",
		"Vector3Int", "Vector2Int", "Units", "UnitsPerInch",
		"new", "this", "base", "null", "true", "false", "var", "out", "ref", "in",
		"is", "as", "return", "if", "else", "float", "int", "double",
		"Zero", "One", "Up", "Down", "Left", "Right", "Forward", "Backward",
		"Normal", "Length", "LengthSquared", "x", "y", "z", "w",
	};

	//A name as it appears in code.
	private static readonly Regex Name = new( @"\b[A-Za-z_]\w*\b", RegexOptions.Compiled );

	//Reads a file and returns what each name in it measures, as far as the engine surface it touches says so.
	internal static Dictionary<string, PortDimension> Learn( string[] code, Func<string, PortDimension> veto )
	{
		var learnt = new Dictionary<string, PortDimension>( StringComparer.Ordinal );

		//Three passes so names learnt late in the file apply to earlier lines; a work bound, not a proof
		for ( var pass = 0; pass < 3; pass++ )
		{
			var before = learnt.Count;

			foreach ( var line in code )
			{
				FromCalls( line, learnt, veto );
				FromMembers( line, learnt, veto );
				FromReceivers( line, learnt, veto );
			}

			if ( learnt.Count == before ) break;
		}

		return learnt;
	}

	//Names handed to a call the engine has written down.
	private static void FromCalls( string line, Dictionary<string, PortDimension> learnt,
		Func<string, PortDimension> veto )
	{
		foreach ( Match call in Name.Matches( line ) )
		{
			if ( !Calls.TryGetValue( call.Value, out var parameters ) ) continue;

			var open = call.Index + call.Length;
			while ( open < line.Length && line[open] == ' ' ) open++;
			if ( open >= line.Length || line[open] != '(' ) continue;

			var arguments = Arguments( line, open );

			for ( var i = 0; i < arguments.Count && i < parameters.Length; i++ )
			{
				Mark( arguments[i], parameters[i], learnt, veto );
			}
		}
	}

	//Names on the other side of an engine member whose dimension is known - assigned to it, or compared against it.
	private static void FromMembers( string line, Dictionary<string, PortDimension> learnt,
		Func<string, PortDimension> veto )
	{
		foreach ( Match member in Name.Matches( line ) )
		{
			if ( member.Index == 0 || line[member.Index - 1] != '.' ) continue;

			var dimension = SceneUnits.Dimension( "", member.Value );
			if ( dimension is PortDimension.Unknown or PortDimension.None ) continue;

			//What follows it: an assignment takes the rest of the statement, a comparison takes as far as the next bracket or separator.
			var after = member.Index + member.Length;
			var rest = line[after..];

			var assign = Regex.Match( rest, @"^\s*=(?!=)(?<value>[^;]*)" );
			if ( assign.Success )
			{
				Mark( assign.Groups["value"].Value, dimension, learnt, veto );
				continue;
			}

			var compare = Regex.Match( rest, @"^\s*(?:\.\w+\s*)?(?:[<>]=?|==|!=)(?<value>[^;,)&|]*)" );
			if ( compare.Success ) Mark( compare.Groups["value"].Value, dimension, learnt, veto );
		}
	}

	//Names handed to a method that means whatever it is called on means.
	private static void FromReceivers( string line, Dictionary<string, PortDimension> learnt,
		Func<string, PortDimension> veto )
	{
		foreach ( Match call in Regex.Matches( line, @"(?<receiver>\b[A-Za-z_]\w*)\s*(?:\.\s*\w+\s*)*?\.\s*(?<method>\w+)\s*\(" ) )
		{
			if ( !Carries.Contains( call.Groups["method"].Value ) ) continue;

			var receiver = call.Groups["receiver"].Value;

			var dimension = SceneUnits.Dimension( "", receiver );

			if ( dimension is PortDimension.Unknown &&
				!learnt.TryGetValue( receiver, out dimension ) )
				continue;

			if ( dimension is PortDimension.None or PortDimension.Unknown ) continue;

			var open = line.IndexOf( '(', call.Index + call.Length - 1 );
			if ( open < 0 ) continue;

			var arguments = Arguments( line, open );
			if ( arguments.Count > 0 ) Mark( arguments[0], dimension, learnt, veto );
		}
	}

	//Marks every game name in an expression with the dimension; the vocabulary vetoes shares/ratios
	private static void Mark( string expression, PortDimension dimension,
		Dictionary<string, PortDimension> learnt, Func<string, PortDimension> veto )
	{
		if ( dimension is PortDimension.None or PortDimension.Unknown ) return;

		foreach ( Match name in Name.Matches( expression ) )
		{
			var text = name.Value;

			if ( NotNames.Contains( text ) ) continue;

			//A call rather than a value.
			var after = name.Index + name.Length;
			while ( after < expression.Length && expression[after] == ' ' ) after++;
			if ( after < expression.Length && expression[after] == '(' ) continue;

			//Something read off another thing - its own member says what it is, not the place this one is being used.
			if ( name.Index > 0 && expression[name.Index - 1] == '.' ) continue;

			//A multiplier is not the measurement: in camPos - camForward * 20 only the 20 is a distance
			if ( Scales( expression, after ) ) continue;

			if ( veto( text ) is PortDimension.None ) continue;

			if ( learnt.TryGetValue( text, out var had ) && had != PortDimension.Unknown ) continue;

			learnt[text] = dimension;
		}
	}

	//Whether the name ending here scales what follows (left of *), skipping brackets
	private static bool Scales( string expression, int after )
	{
		for ( var i = after; i < expression.Length; i++ )
		{
			if ( expression[i] is ' ' or ')' ) continue;

			return expression[i] == '*';
		}

		return false;
	}

	//The arguments of a call, split at the commas that belong to it.
	private static List<string> Arguments( string line, int open )
		=> [.. ArgumentSpans( line, open ).Select( x => line.Substring( x.Start, x.Length ) )];

	//Argument spans in a line that known calls measure, so numbers inside can be converted in place
	internal static List<(int Start, int Length, PortDimension Dimension, string Call, int At)> MeasuredArguments( string line )
	{
		var found = new List<(int Start, int Length, PortDimension Dimension, string Call, int At)>();

		foreach ( Match call in Name.Matches( line ) )
		{
			if ( !Calls.TryGetValue( call.Value, out var parameters ) ) continue;
			if ( Constructors.Contains( call.Value ) && !Constructed( line, call.Index ) ) continue;
			if ( Handed.Contains( call.Value ) && Constructed( line, call.Index ) ) continue;

			var spans = ArgumentsAt( line, call.Index + call.Length );

			for ( var i = 0; i < spans.Count && i < parameters.Length; i++ )
			{
				var dimension = parameters[i];

				if ( dimension is PortDimension.None or PortDimension.Unknown ) continue;

				found.Add( ( spans[i].Start, spans[i].Length, dimension, call.Value, call.Index ) );
			}
		}

		return found;
	}

	//Table rows that are constructors: only with new in front do their arguments get measured
	private static readonly HashSet<string> Constructors = new( StringComparer.Ordinal )
	{
		"Sphere", "Capsule", "Transform", "Vertex",
	};

	//Rows that are a value being handed over rather than one being built, so the word means the table only when there is no new in front of it.
	private static readonly HashSet<string> Handed = new( StringComparer.Ordinal )
	{
		"BBox",
	};

	//Whether this builds an engine shape, so it's already in metres and must not convert again
	internal static bool Constructs( string expression )
	{
		foreach ( Match call in Name.Matches( expression ) )
		{
			if ( !Constructors.Contains( call.Value ) ) continue;
			if ( Constructed( expression, call.Index ) ) return true;
		}

		return false;
	}

	private static bool Constructed( string line, int at )
	{
		var i = at - 1;

		while ( i >= 0 && line[i] == ' ' ) i--;

		return i >= 2 && line[( i - 2 )..( i + 1 )] == "new";
	}

	//Where the arguments of a named call are in this line, for a caller that already knows which call it is looking for.
	internal static List<(int Start, int Length)> ArgumentsOf( string line, string name )
	{
		foreach ( Match call in Name.Matches( line ) )
		{
			if ( call.Value != name ) continue;

			var spans = ArgumentsAt( line, call.Index + call.Length );

			if ( spans.Count > 0 ) return spans;
		}

		return [];
	}

	//The arguments of the bracket that opens here, if one does.
	private static List<(int Start, int Length)> ArgumentsAt( string line, int after )
	{
		var open = after;

		while ( open < line.Length && line[open] == ' ' ) open++;

		if ( open >= line.Length || line[open] != '(' ) return [];

		return ArgumentSpans( line, open );
	}

	private static List<(int Start, int Length)> ArgumentSpans( string line, int open )
	{
		var arguments = new List<(int Start, int Length)>( 4 );

		var depth = 0;
		var start = open + 1;

		for ( var i = open; i < line.Length; i++ )
		{
			var c = line[i];

			if ( c is '(' or '[' ) depth++;
			else if ( c is ')' or ']' )
			{
				depth--;

				if ( depth == 0 )
				{
					arguments.Add( (start, i - start) );
					return arguments;
				}
			}
			else if ( c == ',' && depth == 1 )
			{
				arguments.Add( (start, i - start) );
				start = i + 1;
			}
		}

		//A call whose brackets close on another line. Everything after the opening one is still
		//worth reading - a trace built over four lines is the common shape, not the odd one.
		if ( start < line.Length ) arguments.Add( (start, line.Length - start) );

		return arguments;
	}
}
