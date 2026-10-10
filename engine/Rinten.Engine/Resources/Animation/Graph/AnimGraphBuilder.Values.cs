using System.Text.Json.Nodes;

namespace Rinten;

/// <summary>Graph nodes that produce values (numbers, flags, names, directions) rather than poses</summary>
public sealed partial class AnimGraphBuilder
{
	//-- constants ------------------------------------------------------------

	public Node Constant( float value )
		=> Add( NodeKind.Float, "Const", new JsonObject { ["Value"] = value } );

	public Node Constant( bool value )
		=> Add( NodeKind.Bool, "ConstBool", new JsonObject { ["Value"] = value } );

	public Node Constant( Vector3 value )
		=> Add( NodeKind.Vector, "ConstVector", new JsonObject
		{
			["X"] = value.x,
			["Y"] = value.y,
			["Z"] = value.z,
		} );

	public Node ConstantId( string value = null )
		=> Add( NodeKind.Id, "ConstId", new JsonObject { ["Value"] = value ?? "" } );

	public Node ConstantTarget( Vector3 position, Rotation rotation )
		=> Add( NodeKind.Target, "ConstTarget", new JsonObject
		{
			["Position"] = Write( position ),
			["Rotation"] = Write( rotation ),
		} );

	public Node ConstantTarget( Transform transform )
		=> ConstantTarget( transform.Position, transform.Rotation );

	/// <summary>A target that is deliberately not set - what an IK node reads when nothing is aiming it.</summary>
	public Node NullTarget() => Add( NodeKind.Target, "NullTarget" );

	public Node UnsetTarget() => NullTarget();

	//-- arithmetic ----------------------------------------------------------- The editor compiles all of these through FloatMath, which carries
	//the operation as a value. The named ones are for code building a graph by hand, where Add( a, b ) reads better than an operation enum.

	public Node FloatMath( FloatMathOperation operation, Node a, FloatMathSettings settings = null )
	{
		settings ??= new FloatMathSettings();

		return Add( NodeKind.Float, "FloatMath", new JsonObject
		{
			["Operation"] = operation.ToString(),
			["A"] = Ref( a ),
			["B"] = Ref( settings.B ),
			["ValueB"] = settings.ValueB,
			["Absolute"] = settings.AbsoluteResult,
			["Negate"] = settings.NegateResult,
		} );
	}

	private Node Math( FloatMathOperation operation, Node a, Node b )
		=> FloatMath( operation, a, new FloatMathSettings { B = b } );

	private Node Math( FloatMathOperation operation, Node a, float b )
		=> FloatMath( operation, a, new FloatMathSettings { ValueB = b } );

	public Node Add( Node a, Node b ) => Math( FloatMathOperation.Add, a, b );
	public Node Add( Node a, float b ) => Math( FloatMathOperation.Add, a, b );

	public Node Subtract( Node a, Node b ) => Math( FloatMathOperation.Subtract, a, b );
	public Node Subtract( Node a, float b ) => Math( FloatMathOperation.Subtract, a, b );

	public Node Multiply( Node a, Node b ) => Math( FloatMathOperation.Multiply, a, b );
	public Node Multiply( Node a, float b ) => Math( FloatMathOperation.Multiply, a, b );

	public Node Divide( Node a, Node b ) => Math( FloatMathOperation.Divide, a, b );
	public Node Divide( Node a, float b ) => Math( FloatMathOperation.Divide, a, b );

	public Node Modulo( Node a, Node b ) => Math( FloatMathOperation.Modulo, a, b );
	public Node Modulo( Node a, float b ) => Math( FloatMathOperation.Modulo, a, b );

	public Node Abs( Node value ) => FloatMath( FloatMathOperation.Abs, value );
	public Node Negate( Node value ) => FloatMath( FloatMathOperation.Negate, value );
	public Node Floor( Node value ) => FloatMath( FloatMathOperation.Floor, value );
	public Node Ceiling( Node value ) => FloatMath( FloatMathOperation.Ceiling, value );
	public Node Fraction( Node value ) => FloatMath( FloatMathOperation.FractionalPart, value );

	/// <summary>Angles, which wrap - so clamping one is not clamping a number.</summary>
	public Node AngleMath( Node value, AngleOperation operation )
		=> Add( NodeKind.Float, "AngleMath", new JsonObject
		{
			["Value"] = Ref( value ),
			["Operation"] = operation.ToString(),
		} );

	public Node Clamp( Node value, float min, float max )
		=> Add( NodeKind.Float, "Clamp", new JsonObject
		{
			["Value"] = Ref( value ),
			["Min"] = min,
			["Max"] = max,
		} );

	/// <summary>From one range onto another. Both are given as a pair rather than four numbers, because they are two ranges and not four unrelated bounds.</summary>
	public Node Remap( Node value, Vector2 from, Vector2 to )
		=> Add( NodeKind.Float, "Remap", new JsonObject
		{
			["Value"] = Ref( value ),
			["FromMin"] = from.x,
			["FromMax"] = from.y,
			["ToMin"] = to.x,
			["ToMax"] = to.y,
		} );

	/// <summary>A curve through points, each a time and a value. The four-number form carries the tangents either side as well.</summary>
	public Node Curve( Node value, params (float Time, float Value)[] points )
	{
		var array = new JsonArray();

		foreach ( var (time, height) in points ?? Array.Empty<(float, float)>() )
		{
			array.Add( new JsonObject { ["T"] = time, ["V"] = height, ["In"] = 0.0f, ["Out"] = 0.0f } );
		}

		return Add( NodeKind.Float, "Curve", new JsonObject
		{
			["Value"] = Ref( value ),
			["Points"] = array,
		} );
	}

	public Node Curve( Node value, params (float Time, float Value, float In, float Out)[] points )
	{
		var array = new JsonArray();

		foreach ( var (time, height, tangentIn, tangentOut) in points ?? Array.Empty<(float, float, float, float)>() )
		{
			array.Add( new JsonObject { ["T"] = time, ["V"] = height, ["In"] = tangentIn, ["Out"] = tangentOut } );
		}

		return Add( NodeKind.Float, "Curve", new JsonObject
		{
			["Value"] = Ref( value ),
			["Points"] = array,
		} );
	}

	//-- smoothing ------------------------------------------------------------

	/// <summary>A value that takes time to get where it is going, along a curve.</summary>
	public Node Ease( Node value, EaseSettings settings = null )
	{
		settings ??= new EaseSettings();

		return Add( value is { IsValid: true } ? value.Kind : NodeKind.Float, "Ease", new JsonObject
		{
			["Value"] = Ref( value ),
			["Time"] = settings.Time,
			["Easing"] = settings.Easing.ToString(),
			["UseStartValue"] = settings.UseStartValue,
			["StartValue"] = settings.StartValue,
		} );
	}

	/// <summary>The same, as a spring rather than a curve: it overshoots and settles instead of arriving exactly when it was told to.</summary>
	public Node Spring( Node value, SpringSettings settings = null )
	{
		settings ??= new SpringSettings();

		return Add( value is { IsValid: true } ? value.Kind : NodeKind.Float, "Spring", new JsonObject
		{
			["Value"] = Ref( value ),
			["Hertz"] = settings.Hertz,
			["Damping"] = settings.DampingRatio,
			["UseStartValue"] = settings.UseStartValue,
			["StartValue"] = settings.StartValue,
		} );
	}

	/// <summary>A value read once and kept - on the first update, or every time the graph enters the state this sits in.</summary>
	public Node Cached( Node value, bool onInit )
		=> Add( value is { IsValid: true } ? value.Kind : NodeKind.Float, "Cached", new JsonObject
		{
			["Value"] = Ref( value ),
			["OnInit"] = onInit,
		} );

	//-- logic ----------------------------------------------------------------

	public Node Compare( Node value, FloatComparison comparison, Node comparand, float epsilon = 0.0f )
		=> Add( NodeKind.Bool, "Compare", new JsonObject
		{
			["Value"] = Ref( value ),
			["Comparison"] = comparison.ToString(),
			["Comparand"] = Ref( comparand ),
			["Epsilon"] = epsilon,
		} );

	public Node Compare( Node value, FloatComparison comparison, float comparand, float epsilon = 0.0f )
		=> Add( NodeKind.Bool, "Compare", new JsonObject
		{
			["Value"] = Ref( value ),
			["Comparison"] = comparison.ToString(),
			["Comparand"] = -1,
			["ComparandValue"] = comparand,
			["Epsilon"] = epsilon,
		} );

	public Node InRange( Node value, float min, float max, bool inclusive = true )
		=> Add( NodeKind.Bool, "InRange", new JsonObject
		{
			["Value"] = Ref( value ),
			["Min"] = min,
			["Max"] = max,
			["Inclusive"] = inclusive,
		} );

	public Node Not( Node value )
		=> Add( NodeKind.Bool, "Not", new JsonObject { ["Value"] = Ref( value ) } );

	public Node And( params Node[] values )
		=> Add( NodeKind.Bool, "And", new JsonObject { ["Values"] = Refs( values ) } );

	public Node Or( params Node[] values )
		=> Add( NodeKind.Bool, "Or", new JsonObject { ["Values"] = Refs( values ) } );

	/// <summary>One of two values, by a condition.</summary>
	public Node Switch( Node condition, Node whenTrue, Node whenFalse )
		=> Add( whenTrue is { IsValid: true } ? whenTrue.Kind : NodeKind.Float, "Switch", new JsonObject
		{
			["Condition"] = Ref( condition ),
			["True"] = Ref( whenTrue ),
			["False"] = Ref( whenFalse ),
		} );

	public Node Switch( Node condition, float whenTrue, float whenFalse )
		=> Switch( condition, Constant( whenTrue ), Constant( whenFalse ) );

	//-- names ---------------------------------------------------------------- An id is a name used as
	//a value: a hold type, a weapon, a footstep surface. Compared and switched on rather than measured.

	public Node IdSwitch( Node condition, IdSwitchSettings settings )
	{
		settings ??= new IdSwitchSettings();

		return Add( NodeKind.Id, "IdSwitch", new JsonObject
		{
			["Condition"] = Ref( condition ),
			["True"] = Ref( settings.WhenTrue ),
			["TrueValue"] = settings.TrueValue ?? "",
			["False"] = Ref( settings.WhenFalse ),
			["FalseValue"] = settings.FalseValue ?? "",
		} );
	}

	public Node IdSwitch( Node condition, Node whenTrue, Node whenFalse )
		=> IdSwitch( condition, new IdSwitchSettings { WhenTrue = whenTrue, WhenFalse = whenFalse } );

	public Node IdSwitch( Node condition, string whenTrue, string whenFalse )
		=> IdSwitch( condition, new IdSwitchSettings { TrueValue = whenTrue, FalseValue = whenFalse } );

	public Node IdCompare( Node value, string id, bool matches = true )
		=> IdCompare( value, new[] { id }, matches );

	/// <summary>Whether an id is one of these - or, when asked the other way round, whether it is none of them.</summary>
	public Node IdCompare( Node value, IEnumerable<string> ids, bool matches = true )
		=> Add( NodeKind.Bool, "IdCompare", new JsonObject
		{
			["Value"] = Ref( value ),
			["Ids"] = Text( ids ),
			["Matches"] = matches,
		} );

	/// <summary>The first id whose condition is true.</summary>
	public Node IdSelect( IEnumerable<(Node Condition, string Id)> cases, string fallback = null )
	{
		var array = new JsonArray();

		foreach ( var (condition, id) in cases ?? Array.Empty<(Node, string)>() )
		{
			array.Add( new JsonObject { ["Condition"] = Ref( condition ), ["Id"] = id ?? "" } );
		}

		return Add( NodeKind.Id, "IdSelect", new JsonObject
		{
			["Cases"] = array,
			["Default"] = fallback ?? "",
		} );
	}

	/// <summary>A number per id, and one for the ids nothing lists.</summary>
	public Node IdToFloat( Node value, IEnumerable<(string Id, float Value)> mappings, float fallback = 0.0f )
	{
		var array = new JsonArray();

		foreach ( var (id, mapped) in mappings ?? Array.Empty<(string, float)>() )
		{
			array.Add( new JsonObject { ["Id"] = id ?? "", ["Value"] = mapped } );
		}

		return Add( NodeKind.Float, "IdToFloat", new JsonObject
		{
			["Value"] = Ref( value ),
			["Mappings"] = array,
			["Default"] = fallback,
		} );
	}

	/// <summary>The value belonging to the first case whose condition is true, eased towards rather than jumped to.</summary>
	public Node FloatSelector( IEnumerable<(Node Condition, float Value)> cases,
		float fallback = 0.0f, float easeTime = 0.0f, Easing easing = Easing.Linear )
	{
		var array = new JsonArray();

		foreach ( var (condition, value) in cases ?? Array.Empty<(Node, float)>() )
		{
			array.Add( new JsonObject { ["Condition"] = Ref( condition ), ["Value"] = value } );
		}

		return Add( NodeKind.Float, "FloatSelect", new JsonObject
		{
			["Cases"] = array,
			["Default"] = fallback,
			["EaseTime"] = easeTime,
			["Easing"] = easing.ToString(),
		} );
	}

	//-- directions -----------------------------------------------------------

	public Node VectorCreate( Node x, Node y, Node z, Node from = default )
		=> Add( NodeKind.Vector, "VectorCreate", new JsonObject
		{
			["X"] = Ref( x ),
			["Y"] = Ref( y ),
			["Z"] = Ref( z ),
			["From"] = Ref( from ),
		} );

	public Node VectorInfo( Node value, VectorComponent component )
		=> Add( NodeKind.Float, "VectorInfo", new JsonObject
		{
			["Value"] = Ref( value ),
			["Component"] = component.ToString(),
		} );

	public Node VectorNegate( Node value )
		=> Add( NodeKind.Vector, "VectorNegate", new JsonObject { ["Value"] = Ref( value ) } );

	//-- targets -------------------------------------------------------------- A target is a place to reach for:
	//where a hand should end up, what to look at. A transform with the extra state of not being set at all.

	public Node BoneTarget( string bone )
		=> Add( NodeKind.Target, "BoneTarget", new JsonObject { ["Bone"] = bone ?? "" } );

	public Node IsTargetSet( Node target )
		=> Add( NodeKind.Bool, "IsTargetSet", new JsonObject { ["Target"] = Ref( target ) } );

	public Node TargetInfo( Node target, TargetInfoType info, bool worldSpace = false )
		=> Add( NodeKind.Float, "TargetInfo", new JsonObject
		{
			["Target"] = Ref( target ),
			["Info"] = info.ToString(),
			["WorldSpace"] = worldSpace,
		} );

	/// <summary>A target at a point the graph works out, rather than on a bone.</summary>
	public Node PointTarget( Node position )
		=> Add( NodeKind.Target, "PointTarget", new JsonObject { ["Position"] = Ref( position ) } );

	public Node TargetPoint( Node target, bool worldSpace = false )
		=> Add( NodeKind.Vector, "TargetPoint", new JsonObject
		{
			["Target"] = Ref( target ),
			["WorldSpace"] = worldSpace,
		} );

	public Node TargetOffset( Node target, Vector3 position, Rotation rotation, bool boneSpace = false )
		=> Add( NodeKind.Target, "TargetOffset", new JsonObject
		{
			["Target"] = Ref( target ),
			["Position"] = Write( position ),
			["Rotation"] = Write( rotation ),
			["BoneSpace"] = boneSpace,
		} );

	/// <summary>Whichever of several targets is nearest what is wanted, by position and by which way it faces.</summary>
	public Node TargetSelector( Node target, IEnumerable<Node> options, TargetSelectorSettings settings = null )
	{
		settings ??= new TargetSelectorSettings();

		return Add( NodeKind.Target, "TargetSelect", new JsonObject
		{
			["Target"] = Ref( target ),
			["Options"] = Refs( options ),
			["PositionWeight"] = settings.PositionWeight,
			["OrientationWeight"] = settings.OrientationWeight,
			["WorldSpace"] = settings.WorldSpace,
			["IgnoreInvalid"] = settings.IgnoreInvalidOptions,
		} );
	}

	//-- shared ---------------------------------------------------------------

	private static JsonArray Write( Vector3 value ) => new() { value.x, value.y, value.z };

	private static JsonArray Write( Rotation value ) => new() { value.x, value.y, value.z, value.w };
}
