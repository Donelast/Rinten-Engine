using System.Text.Json.Nodes;

namespace Rinten.Animation;

//The nodes that decide: numbers, flags, names and directions. All of these run on the managed side. They are read a few dozen times
//a frame per character and each is a handful of arithmetic - the frame goes into blending poses, not into deciding which ones.
internal static class ValueNodes
{
	//An input, or null when nothing was connected.
	internal static GraphNode Get( GraphNode[] nodes, int index )
		=> index >= 0 && index < nodes.Length ? nodes[index] : null;
}

internal sealed class ConstFloatNode : GraphNode
{
	private readonly float value;

	public ConstFloatNode( JsonObject node ) => value = GraphFields.Number( node, "Value" );

	protected override float OnFloat( GraphContext context ) => value;
	protected override bool OnBool( GraphContext context ) => value != 0.0f;
}

internal sealed class ConstBoolNode : GraphNode
{
	private readonly bool value;

	public ConstBoolNode( JsonObject node ) => value = GraphFields.Flag( node, "Value" );

	protected override bool OnBool( GraphContext context ) => value;
	protected override float OnFloat( GraphContext context ) => value ? 1.0f : 0.0f;
}

internal sealed class ConstIdNode : GraphNode
{
	private readonly string value;

	public ConstIdNode( JsonObject node ) => value = GraphFields.Text( node, "Value" );

	protected override string OnId( GraphContext context ) => value;
}

internal sealed class ConstVectorNode : GraphNode
{
	private readonly Vector3 value;

	public ConstVectorNode( JsonObject node )
		=> value = new Vector3( GraphFields.Number( node, "X" ), GraphFields.Number( node, "Y" ), GraphFields.Number( node, "Z" ) );

	protected override Vector3 OnVector( GraphContext context ) => value;
}

internal sealed class ConstTargetNode : GraphNode
{
	private readonly Transform value;
	private readonly bool set;

	public ConstTargetNode( JsonObject node, bool isSet )
	{
		set = isSet;

		if ( !isSet ) return;

		var position = GraphFields.Array( node, "Position" );
		var rotation = GraphFields.Array( node, "Rotation" );

		value = new Transform(
			position is null ? Vector3.Zero : new Vector3( Read( position, 0 ), Read( position, 1 ), Read( position, 2 ) ),
			rotation is null ? Rotation.Identity : new Rotation( Read( rotation, 0 ), Read( rotation, 1 ), Read( rotation, 2 ), Read( rotation, 3 ) ) );
	}

	private static float Read( JsonArray array, int index )
		=> index < array.Count && array[index] is { } value ? value.GetValue<float>() : 0.0f;

	protected override Transform OnTarget( GraphContext context ) => value;
	public override bool HasTarget( GraphContext context ) => set;
	protected override bool OnBool( GraphContext context ) => set;
}

//What the game set, or what the graph worked out under that name.
internal sealed class ParameterNode : GraphNode
{
	private readonly int parameter;

	public ParameterNode( JsonObject node ) => parameter = GraphFields.Integer( node, "Parameter", -1 );

	protected override float OnFloat( GraphContext context ) => Owner.GetFloat( parameter );
	protected override bool OnBool( GraphContext context ) => Owner.GetBool( parameter );
	protected override string OnId( GraphContext context ) => Owner.GetId( parameter );
	protected override Vector3 OnVector( GraphContext context ) => Owner.GetVector( parameter );
	protected override Transform OnTarget( GraphContext context ) => Owner.GetTarget( parameter );
	public override bool HasTarget( GraphContext context ) => Owner.HasTarget( parameter );
}

internal sealed class FloatMathNode : GraphNode
{
	private readonly AnimGraphBuilder.FloatMathOperation operation;
	private readonly int a, b;
	private readonly float valueB;
	private readonly bool absolute, negate;

	private GraphNode left, right;

	public FloatMathNode( JsonObject node )
	{
		operation = GraphFields.Enum( node, "Operation", AnimGraphBuilder.FloatMathOperation.Add );

		a = GraphFields.Reference( node, "A" );
		b = GraphFields.Reference( node, "B" );

		valueB = GraphFields.Number( node, "ValueB" );
		absolute = GraphFields.Flag( node, "Absolute" );
		negate = GraphFields.Flag( node, "Negate" );
	}

	public override void Link( GraphNode[] nodes )
	{
		left = ValueNodes.Get( nodes, a );
		right = ValueNodes.Get( nodes, b );
	}

	protected override float OnFloat( GraphContext context )
	{
		var x = left?.Float( context ) ?? 0.0f;

		//A connected second input wins over the number written beside it, which is what the editor shows when nothing is plugged in.
		var y = right is null ? valueB : right.Float( context );

		var result = operation switch
		{
			AnimGraphBuilder.FloatMathOperation.Add => x + y,
			AnimGraphBuilder.FloatMathOperation.Subtract => x - y,
			AnimGraphBuilder.FloatMathOperation.Multiply => x * y,
			AnimGraphBuilder.FloatMathOperation.Divide => MathF.Abs( y ) < 1e-8f ? 0.0f : x / y,
			AnimGraphBuilder.FloatMathOperation.Modulo => MathF.Abs( y ) < 1e-8f ? 0.0f : x % y,
			AnimGraphBuilder.FloatMathOperation.Abs => MathF.Abs( x ),
			AnimGraphBuilder.FloatMathOperation.Negate => -x,
			AnimGraphBuilder.FloatMathOperation.Floor => MathF.Floor( x ),
			AnimGraphBuilder.FloatMathOperation.Ceiling => MathF.Ceiling( x ),
			AnimGraphBuilder.FloatMathOperation.IntegerPart => MathF.Truncate( x ),
			AnimGraphBuilder.FloatMathOperation.FractionalPart => x - MathF.Truncate( x ),
			AnimGraphBuilder.FloatMathOperation.InverseFractionalPart => 1.0f - ( x - MathF.Truncate( x ) ),
			_ => x,
		};

		if ( absolute ) result = MathF.Abs( result );
		if ( negate ) result = -result;

		return result;
	}

	protected override bool OnBool( GraphContext context ) => Float( context ) != 0.0f;
}

//Angles, which wrap - so clamping one is not clamping a number.
internal sealed class AngleMathNode : GraphNode
{
	private readonly AnimGraphBuilder.AngleOperation operation;
	private readonly int value;

	private GraphNode input;

	public AngleMathNode( JsonObject node )
	{
		operation = GraphFields.Enum( node, "Operation", AnimGraphBuilder.AngleOperation.ClampTo180 );
		value = GraphFields.Reference( node, "Value" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override float OnFloat( GraphContext context )
	{
		var angle = input?.Float( context ) ?? 0.0f;

		switch ( operation )
		{
			case AnimGraphBuilder.AngleOperation.ClampTo180:
				return Wrap( angle, -180.0f );

			case AnimGraphBuilder.AngleOperation.ClampTo360:
				return Wrap( angle, 0.0f );

			case AnimGraphBuilder.AngleOperation.FlipHemisphere:
			{
				//Behind becomes the mirror of in front, which is how a look that would break the neck becomes one that does not.
				var wrapped = Wrap( angle, -180.0f );
				return wrapped > 90.0f ? 180.0f - wrapped : wrapped < -90.0f ? -180.0f - wrapped : wrapped;
			}

			case AnimGraphBuilder.AngleOperation.FlipHemisphereNegate:
			{
				var wrapped = Wrap( angle, -180.0f );
				var flipped = wrapped > 90.0f ? 180.0f - wrapped : wrapped < -90.0f ? -180.0f - wrapped : wrapped;

				return -flipped;
			}
		}

		return angle;
	}

	//Into a full turn starting wherever it is asked to.
	private static float Wrap( float angle, float from )
	{
		var wrapped = ( angle - from ) % 360.0f;
		if ( wrapped < 0.0f ) wrapped += 360.0f;

		return wrapped + from;
	}
}

internal sealed class ClampNode : GraphNode
{
	private readonly int value;
	private readonly float min, max;

	private GraphNode input;

	public ClampNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		min = GraphFields.Number( node, "Min" );
		max = GraphFields.Number( node, "Max" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override float OnFloat( GraphContext context )
		=> Math.Clamp( input?.Float( context ) ?? 0.0f, MathF.Min( min, max ), MathF.Max( min, max ) );
}

internal sealed class RemapNode : GraphNode
{
	private readonly int value;
	private readonly float fromMin, fromMax, toMin, toMax;

	private GraphNode input;

	public RemapNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );

		fromMin = GraphFields.Number( node, "FromMin" );
		fromMax = GraphFields.Number( node, "FromMax" );
		toMin = GraphFields.Number( node, "ToMin" );
		toMax = GraphFields.Number( node, "ToMax" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override float OnFloat( GraphContext context )
	{
		var span = fromMax - fromMin;

		//A range of no width has no answer but its own start; dividing would give an infinity that spreads through everything downstream.
		if ( MathF.Abs( span ) < 1e-8f ) return toMin;

		var at = Math.Clamp( ( ( input?.Float( context ) ?? 0.0f ) - fromMin ) / span, 0.0f, 1.0f );

		return toMin + ( ( toMax - toMin ) * at );
	}
}

//A curve through points, read at a value.
internal sealed class CurveNode : GraphNode
{
	private readonly int value;
	private readonly float[] times;
	private readonly float[] values;

	private GraphNode input;

	public CurveNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );

		var points = GraphFields.Array( node, "Points" );
		var count = points?.Count ?? 0;

		times = new float[count];
		values = new float[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( points[i] is not JsonObject point ) continue;

			times[i] = GraphFields.Number( point, "T" );
			values[i] = GraphFields.Number( point, "V" );
		}
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override float OnFloat( GraphContext context )
	{
		if ( times.Length == 0 ) return 0.0f;

		var at = input?.Float( context ) ?? 0.0f;

		if ( at <= times[0] ) return values[0];
		if ( at >= times[^1] ) return values[^1];

		for ( var i = 0; i + 1 < times.Length; i++ )
		{
			if ( at < times[i] || at > times[i + 1] ) continue;

			var span = times[i + 1] - times[i];
			if ( MathF.Abs( span ) < 1e-8f ) return values[i];

			var alpha = ( at - times[i] ) / span;

			return values[i] + ( ( values[i + 1] - values[i] ) * alpha );
		}

		return values[^1];
	}
}

//A value that takes time to get where it is going. Worked out once a frame and remembered, because it integrates: read twice
//in one frame it would move twice as fast, which is exactly the kind of bug that only shows up when somebody shares a node.
internal sealed class EaseNode : GraphNode
{
	private readonly int value;
	private readonly float time;
	private readonly AnimGraphBuilder.Easing easing;
	private readonly bool useStart;
	private readonly float start;

	private GraphNode input;

	private int frame = -1;
	private float current;
	private float from;
	private float elapsed;
	private float towards;
	private bool started;

	public EaseNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		time = GraphFields.Number( node, "Time" );
		easing = GraphFields.Enum( node, "Easing", AnimGraphBuilder.Easing.Linear );
		useStart = GraphFields.Flag( node, "UseStartValue" );
		start = GraphFields.Number( node, "StartValue" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	public override void Reset( GraphContext context )
	{
		started = false;
		frame = -1;
	}

	protected override float OnFloat( GraphContext context )
	{
		if ( frame == context.Frame ) return current;
		frame = context.Frame;

		var target = input?.Float( context ) ?? 0.0f;

		if ( !started )
		{
			started = true;
			current = useStart ? start : target;
			from = current;
			towards = target;
			elapsed = time;
		}

		if ( target != towards )
		{
			//Somewhere new to go: start again from where it actually is, not from where the last ease began.
			from = current;
			towards = target;
			elapsed = 0.0f;
		}

		if ( time <= 0.0f )
		{
			current = target;
			return current;
		}

		elapsed = MathF.Min( elapsed + context.Delta, time );

		var alpha = Curves.Apply( easing, elapsed / time );
		current = from + ( ( towards - from ) * alpha );

		return current;
	}
}

//The same, as a spring: it overshoots and settles.
internal sealed class SpringNode : GraphNode
{
	private readonly int value;
	private readonly float hertz, damping;
	private readonly bool useStart;
	private readonly float start;

	private GraphNode input;

	private int frame = -1;
	private float current, velocity;
	private bool started;

	public SpringNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		hertz = GraphFields.Number( node, "Hertz", 10.0f );
		damping = GraphFields.Number( node, "Damping", 1.0f );
		useStart = GraphFields.Flag( node, "UseStartValue" );
		start = GraphFields.Number( node, "StartValue" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	public override void Reset( GraphContext context )
	{
		started = false;
		frame = -1;
		velocity = 0.0f;

		vectorStarted = false;
		vectorFrame = -1;
		vectorVelocity = Vector3.Zero;
	}

	protected override float OnFloat( GraphContext context )
	{
		if ( frame == context.Frame ) return current;
		frame = context.Frame;

		var target = input?.Float( context ) ?? 0.0f;

		if ( !started )
		{
			started = true;
			current = useStart ? start : target;
			velocity = 0.0f;
		}

		var delta = context.Delta;
		if ( delta <= 0.0f || hertz <= 0.0f ) return current;

		//Implicit rather than explicit: a stiff spring stepped forwards blows up at a
		//frame rate nobody was testing at, and a stiff spring is the point of having one.
		var omega = 2.0f * MathF.PI * hertz;
		var factor = 1.0f + ( 2.0f * delta * damping * omega );
		var squared = omega * omega * delta;

		var detonator = factor + ( squared * delta );
		if ( MathF.Abs( detonator ) < 1e-8f ) return current;

		current = ( ( factor * current ) + ( delta * velocity ) + ( squared * delta * target ) ) / detonator;
		velocity = ( velocity + ( squared * ( target - current ) ) ) / factor;

		return current;
	}

	private int vectorFrame = -1;
	private Vector3 vectorCurrent, vectorVelocity;
	private bool vectorStarted;

	//Spring smoothing for vectors too (look-at targets come through here)
	protected override Vector3 OnVector( GraphContext context )
	{
		if ( vectorFrame == context.Frame ) return vectorCurrent;
		vectorFrame = context.Frame;

		var target = input?.Vector( context ) ?? Vector3.Zero;

		if ( !vectorStarted )
		{
			vectorStarted = true;
			vectorCurrent = target;
			vectorVelocity = Vector3.Zero;
		}

		var delta = context.Delta;
		if ( delta <= 0.0f || hertz <= 0.0f ) return vectorCurrent;

		var omega = 2.0f * MathF.PI * hertz;
		var factor = 1.0f + ( 2.0f * delta * damping * omega );
		var squared = omega * omega * delta;

		var detonator = factor + ( squared * delta );
		if ( MathF.Abs( detonator ) < 1e-8f ) return vectorCurrent;

		vectorCurrent = ( ( factor * vectorCurrent ) + ( delta * vectorVelocity ) + ( squared * delta * target ) ) / detonator;
		vectorVelocity = ( vectorVelocity + ( squared * ( target - vectorCurrent ) ) ) / factor;

		return vectorCurrent;
	}
}

//A value read once and kept.
internal sealed class CachedNode : GraphNode
{
	private readonly int value;
	private readonly bool onInit;

	private GraphNode input;

	private bool held;
	private float number;
	private bool flag;
	private string id;
	private Vector3 vector;

	public CachedNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		onInit = GraphFields.Flag( node, "OnInit" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	public override void Reset( GraphContext context )
	{
		//A node that caches on init keeps what it has for the life of the graph; one
		//that does not takes a new value every time the state it sits in is entered.
		if ( onInit && held ) return;

		held = false;
	}

	private void Take( GraphContext context )
	{
		if ( held ) return;

		held = true;

		number = input?.Float( context ) ?? 0.0f;
		flag = input?.Bool( context ) ?? false;
		id = input?.Id( context ) ?? "";
		vector = input?.Vector( context ) ?? Vector3.Zero;
	}

	protected override float OnFloat( GraphContext context )
	{
		Take( context );
		return number;
	}

	protected override bool OnBool( GraphContext context )
	{
		Take( context );
		return flag;
	}

	protected override string OnId( GraphContext context )
	{
		Take( context );
		return id;
	}

	protected override Vector3 OnVector( GraphContext context )
	{
		Take( context );
		return vector;
	}
}

internal sealed class CompareNode : GraphNode
{
	private readonly int value, comparand;
	private readonly float comparandValue, epsilon;
	private readonly AnimGraphBuilder.FloatComparison comparison;

	private GraphNode left, right;

	public CompareNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		comparand = GraphFields.Reference( node, "Comparand" );
		comparandValue = GraphFields.Number( node, "ComparandValue" );
		epsilon = GraphFields.Number( node, "Epsilon" );
		comparison = GraphFields.Enum( node, "Comparison", AnimGraphBuilder.FloatComparison.GreaterThanEqual );
	}

	public override void Link( GraphNode[] nodes )
	{
		left = ValueNodes.Get( nodes, value );
		right = ValueNodes.Get( nodes, comparand );
	}

	protected override bool OnBool( GraphContext context )
	{
		var a = left?.Float( context ) ?? 0.0f;
		var b = right is null ? comparandValue : right.Float( context );

		return comparison switch
		{
			AnimGraphBuilder.FloatComparison.GreaterThan => a > b,
			AnimGraphBuilder.FloatComparison.GreaterThanEqual => a >= b,
			AnimGraphBuilder.FloatComparison.LessThan => a < b,
			AnimGraphBuilder.FloatComparison.LessThanEqual => a <= b,
			AnimGraphBuilder.FloatComparison.NearEqual => MathF.Abs( a - b ) <= MathF.Abs( epsilon ),
			_ => false,
		};
	}

	protected override float OnFloat( GraphContext context ) => Bool( context ) ? 1.0f : 0.0f;
}

internal sealed class InRangeNode : GraphNode
{
	private readonly int value;
	private readonly float min, max;
	private readonly bool inclusive;

	private GraphNode input;

	public InRangeNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		min = GraphFields.Number( node, "Min" );
		max = GraphFields.Number( node, "Max" );
		inclusive = GraphFields.Flag( node, "Inclusive", true );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override bool OnBool( GraphContext context )
	{
		var at = input?.Float( context ) ?? 0.0f;
		var low = MathF.Min( min, max );
		var high = MathF.Max( min, max );

		return inclusive ? at >= low && at <= high : at > low && at < high;
	}
}

internal sealed class NotNode : GraphNode
{
	private readonly int value;
	private GraphNode input;

	public NotNode( JsonObject node ) => value = GraphFields.Reference( node, "Value" );

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override bool OnBool( GraphContext context ) => !( input?.Bool( context ) ?? false );
}

internal sealed class LogicNode : GraphNode
{
	private readonly bool all;
	private readonly int[] values;

	private GraphNode[] inputs = Array.Empty<GraphNode>();

	public LogicNode( JsonObject node, bool requireAll )
	{
		all = requireAll;

		var list = GraphFields.Array( node, "Values" );
		values = new int[list?.Count ?? 0];

		for ( var i = 0; i < values.Length; i++ ) values[i] = list[i]?.GetValue<int>() ?? -1;
	}

	public override void Link( GraphNode[] nodes )
	{
		inputs = new GraphNode[values.Length];
		for ( var i = 0; i < values.Length; i++ ) inputs[i] = ValueNodes.Get( nodes, values[i] );
	}

	protected override bool OnBool( GraphContext context )
	{
		//Nothing connected: an "and" of nothing is true and an "or" of nothing is false, which is what makes an empty condition harmless either way.
		if ( inputs.Length == 0 ) return all;

		foreach ( var input in inputs )
		{
			var value = input?.Bool( context ) ?? false;

			if ( all && !value ) return false;
			if ( !all && value ) return true;
		}

		return all;
	}
}

internal sealed class SwitchNode : GraphNode
{
	private readonly int condition, whenTrue, whenFalse;

	private GraphNode test, yes, no;

	public SwitchNode( JsonObject node )
	{
		condition = GraphFields.Reference( node, "Condition" );
		whenTrue = GraphFields.Reference( node, "True" );
		whenFalse = GraphFields.Reference( node, "False" );
	}

	public override void Link( GraphNode[] nodes )
	{
		test = ValueNodes.Get( nodes, condition );
		yes = ValueNodes.Get( nodes, whenTrue );
		no = ValueNodes.Get( nodes, whenFalse );
	}

	private GraphNode Chosen( GraphContext context ) => ( test?.Bool( context ) ?? false ) ? yes : no;

	protected override float OnFloat( GraphContext context ) => Chosen( context )?.Float( context ) ?? 0.0f;
	protected override bool OnBool( GraphContext context ) => Chosen( context )?.Bool( context ) ?? false;
	protected override string OnId( GraphContext context ) => Chosen( context )?.Id( context ) ?? "";
	protected override Vector3 OnVector( GraphContext context ) => Chosen( context )?.Vector( context ) ?? Vector3.Zero;
}

internal sealed class IdSwitchNode : GraphNode
{
	private readonly int condition, whenTrue, whenFalse;
	private readonly string trueValue, falseValue;

	private GraphNode test, yes, no;

	public IdSwitchNode( JsonObject node )
	{
		condition = GraphFields.Reference( node, "Condition" );
		whenTrue = GraphFields.Reference( node, "True" );
		whenFalse = GraphFields.Reference( node, "False" );

		trueValue = GraphFields.Text( node, "TrueValue" );
		falseValue = GraphFields.Text( node, "FalseValue" );
	}

	public override void Link( GraphNode[] nodes )
	{
		test = ValueNodes.Get( nodes, condition );
		yes = ValueNodes.Get( nodes, whenTrue );
		no = ValueNodes.Get( nodes, whenFalse );
	}

	protected override string OnId( GraphContext context )
	{
		if ( test?.Bool( context ) ?? false ) return yes is null ? trueValue : yes.Id( context );

		return no is null ? falseValue : no.Id( context );
	}
}

internal sealed class IdCompareNode : GraphNode
{
	private readonly int value;
	private readonly string[] ids;
	private readonly bool matches;

	private GraphNode input;

	public IdCompareNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		matches = GraphFields.Flag( node, "Matches", true );

		var list = GraphFields.Array( node, "Ids" );
		ids = new string[list?.Count ?? 0];

		for ( var i = 0; i < ids.Length; i++ ) ids[i] = list[i]?.GetValue<string>() ?? "";
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override bool OnBool( GraphContext context )
	{
		var id = input?.Id( context ) ?? "";
		var found = false;

		foreach ( var candidate in ids )
		{
			if ( !string.Equals( candidate, id, StringComparison.OrdinalIgnoreCase ) ) continue;

			found = true;
			break;
		}

		return found == matches;
	}
}

internal sealed class IdSelectValueNode : GraphNode
{
	private readonly int[] conditions;
	private readonly string[] ids;
	private readonly string fallback;

	private GraphNode[] tests = Array.Empty<GraphNode>();

	public IdSelectValueNode( JsonObject node )
	{
		fallback = GraphFields.Text( node, "Default" );

		var cases = GraphFields.Array( node, "Cases" );
		var count = cases?.Count ?? 0;

		conditions = new int[count];
		ids = new string[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( cases[i] is not JsonObject entry ) continue;

			conditions[i] = GraphFields.Reference( entry, "Condition" );
			ids[i] = GraphFields.Text( entry, "Id" );
		}
	}

	public override void Link( GraphNode[] nodes )
	{
		tests = new GraphNode[conditions.Length];
		for ( var i = 0; i < conditions.Length; i++ ) tests[i] = ValueNodes.Get( nodes, conditions[i] );
	}

	protected override string OnId( GraphContext context )
	{
		for ( var i = 0; i < tests.Length; i++ )
		{
			if ( tests[i]?.Bool( context ) ?? false ) return ids[i];
		}

		return fallback;
	}
}

internal sealed class IdToFloatNode : GraphNode
{
	private readonly int value;
	private readonly string[] ids;
	private readonly float[] values;
	private readonly float fallback;

	private GraphNode input;

	public IdToFloatNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		fallback = GraphFields.Number( node, "Default" );

		var mappings = GraphFields.Array( node, "Mappings" );
		var count = mappings?.Count ?? 0;

		ids = new string[count];
		values = new float[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( mappings[i] is not JsonObject entry ) continue;

			ids[i] = GraphFields.Text( entry, "Id" );
			values[i] = GraphFields.Number( entry, "Value" );
		}
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override float OnFloat( GraphContext context )
	{
		var id = input?.Id( context ) ?? "";

		for ( var i = 0; i < ids.Length; i++ )
		{
			if ( string.Equals( ids[i], id, StringComparison.OrdinalIgnoreCase ) ) return values[i];
		}

		return fallback;
	}
}

//The value belonging to the first true case, eased towards rather than jumped to.
internal sealed class FloatSelectNode : GraphNode
{
	private readonly int[] conditions;
	private readonly float[] values;
	private readonly float fallback, easeTime;
	private readonly AnimGraphBuilder.Easing easing;

	private GraphNode[] tests = Array.Empty<GraphNode>();

	private int frame = -1;
	private float current, from, towards, elapsed;
	private bool started;

	public FloatSelectNode( JsonObject node )
	{
		fallback = GraphFields.Number( node, "Default" );
		easeTime = GraphFields.Number( node, "EaseTime" );
		easing = GraphFields.Enum( node, "Easing", AnimGraphBuilder.Easing.Linear );

		var cases = GraphFields.Array( node, "Cases" );
		var count = cases?.Count ?? 0;

		conditions = new int[count];
		values = new float[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( cases[i] is not JsonObject entry ) continue;

			conditions[i] = GraphFields.Reference( entry, "Condition" );
			values[i] = GraphFields.Number( entry, "Value" );
		}
	}

	public override void Link( GraphNode[] nodes )
	{
		tests = new GraphNode[conditions.Length];
		for ( var i = 0; i < conditions.Length; i++ ) tests[i] = ValueNodes.Get( nodes, conditions[i] );
	}

	public override void Reset( GraphContext context ) => started = false;

	protected override float OnFloat( GraphContext context )
	{
		if ( frame == context.Frame ) return current;
		frame = context.Frame;

		var target = fallback;

		for ( var i = 0; i < tests.Length; i++ )
		{
			if ( !( tests[i]?.Bool( context ) ?? false ) ) continue;

			target = values[i];
			break;
		}

		if ( !started )
		{
			started = true;
			current = target;
			towards = target;
			elapsed = easeTime;
		}

		if ( target != towards )
		{
			from = current;
			towards = target;
			elapsed = 0.0f;
		}

		if ( easeTime <= 0.0f )
		{
			current = target;
			return current;
		}

		elapsed = MathF.Min( elapsed + context.Delta, easeTime );
		current = from + ( ( towards - from ) * Curves.Apply( easing, elapsed / easeTime ) );

		return current;
	}
}

internal sealed class VectorCreateNode : GraphNode
{
	private readonly int x, y, z, from;

	private GraphNode xValue, yValue, zValue, source;

	public VectorCreateNode( JsonObject node )
	{
		x = GraphFields.Reference( node, "X" );
		y = GraphFields.Reference( node, "Y" );
		z = GraphFields.Reference( node, "Z" );
		from = GraphFields.Reference( node, "From" );
	}

	public override void Link( GraphNode[] nodes )
	{
		xValue = ValueNodes.Get( nodes, x );
		yValue = ValueNodes.Get( nodes, y );
		zValue = ValueNodes.Get( nodes, z );
		source = ValueNodes.Get( nodes, from );
	}

	protected override Vector3 OnVector( GraphContext context )
	{
		//Whatever is connected replaces that component of what came in, so a node with only Z connected changes the height and nothing else.
		var value = source?.Vector( context ) ?? Vector3.Zero;

		return new Vector3(
			xValue?.Float( context ) ?? value.x,
			yValue?.Float( context ) ?? value.y,
			zValue?.Float( context ) ?? value.z );
	}
}

internal sealed class VectorInfoNode : GraphNode
{
	private readonly int value;
	private readonly AnimGraphBuilder.VectorComponent component;

	private GraphNode input;

	public VectorInfoNode( JsonObject node )
	{
		value = GraphFields.Reference( node, "Value" );
		component = GraphFields.Enum( node, "Component", AnimGraphBuilder.VectorComponent.X );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override float OnFloat( GraphContext context )
	{
		var value = input?.Vector( context ) ?? Vector3.Zero;

		return component switch
		{
			AnimGraphBuilder.VectorComponent.X => value.x,
			AnimGraphBuilder.VectorComponent.Y => value.y,
			AnimGraphBuilder.VectorComponent.Z => value.z,
			AnimGraphBuilder.VectorComponent.Length => value.Length,

			//Degrees, and around the model's own up - which is what every angle in an animation graph means.
			AnimGraphBuilder.VectorComponent.AngleHorizontal
				=> MathF.Atan2( -value.x, -value.z ).RadianToDegree(),

			AnimGraphBuilder.VectorComponent.AngleVertical
				=> MathF.Atan2( value.y, new Vector2( value.x, value.z ).Length ).RadianToDegree(),

			_ => 0.0f,
		};
	}
}

internal sealed class VectorNegateNode : GraphNode
{
	private readonly int value;
	private GraphNode input;

	public VectorNegateNode( JsonObject node ) => value = GraphFields.Reference( node, "Value" );

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, value );

	protected override Vector3 OnVector( GraphContext context ) => -( input?.Vector( context ) ?? Vector3.Zero );
}

//The curves an ease can follow.
internal static class Curves
{
	public static float Apply( AnimGraphBuilder.Easing easing, float t )
	{
		t = Math.Clamp( t, 0.0f, 1.0f );

		return easing switch
		{
			AnimGraphBuilder.Easing.None => t >= 1.0f ? 1.0f : 0.0f,

			AnimGraphBuilder.Easing.InQuad => t * t,
			AnimGraphBuilder.Easing.OutQuad => 1.0f - ( ( 1.0f - t ) * ( 1.0f - t ) ),
			AnimGraphBuilder.Easing.InOutQuad => t < 0.5f ? 2.0f * t * t : 1.0f - ( MathF.Pow( ( -2.0f * t ) + 2.0f, 2.0f ) / 2.0f ),

			AnimGraphBuilder.Easing.InCubic => t * t * t,
			AnimGraphBuilder.Easing.OutCubic => 1.0f - MathF.Pow( 1.0f - t, 3.0f ),
			AnimGraphBuilder.Easing.InOutCubic => t < 0.5f ? 4.0f * t * t * t : 1.0f - ( MathF.Pow( ( -2.0f * t ) + 2.0f, 3.0f ) / 2.0f ),

			AnimGraphBuilder.Easing.InQuart => t * t * t * t,
			AnimGraphBuilder.Easing.OutQuart => 1.0f - MathF.Pow( 1.0f - t, 4.0f ),
			AnimGraphBuilder.Easing.InOutQuart => t < 0.5f ? 8.0f * t * t * t * t : 1.0f - ( MathF.Pow( ( -2.0f * t ) + 2.0f, 4.0f ) / 2.0f ),

			AnimGraphBuilder.Easing.InQuint => MathF.Pow( t, 5.0f ),
			AnimGraphBuilder.Easing.OutQuint => 1.0f - MathF.Pow( 1.0f - t, 5.0f ),
			AnimGraphBuilder.Easing.InOutQuint => t < 0.5f ? 16.0f * MathF.Pow( t, 5.0f ) : 1.0f - ( MathF.Pow( ( -2.0f * t ) + 2.0f, 5.0f ) / 2.0f ),

			AnimGraphBuilder.Easing.InSine => 1.0f - MathF.Cos( ( t * MathF.PI ) / 2.0f ),
			AnimGraphBuilder.Easing.OutSine => MathF.Sin( ( t * MathF.PI ) / 2.0f ),
			AnimGraphBuilder.Easing.InOutSine => -( MathF.Cos( MathF.PI * t ) - 1.0f ) / 2.0f,

			AnimGraphBuilder.Easing.InExpo => t <= 0.0f ? 0.0f : MathF.Pow( 2.0f, ( 10.0f * t ) - 10.0f ),
			AnimGraphBuilder.Easing.OutExpo => t >= 1.0f ? 1.0f : 1.0f - MathF.Pow( 2.0f, -10.0f * t ),
			AnimGraphBuilder.Easing.InOutExpo => t <= 0.0f ? 0.0f
				: t >= 1.0f ? 1.0f
				: t < 0.5f ? MathF.Pow( 2.0f, ( 20.0f * t ) - 10.0f ) / 2.0f
				: ( 2.0f - MathF.Pow( 2.0f, ( -20.0f * t ) + 10.0f ) ) / 2.0f,

			AnimGraphBuilder.Easing.InCirc => 1.0f - MathF.Sqrt( 1.0f - ( t * t ) ),
			AnimGraphBuilder.Easing.OutCirc => MathF.Sqrt( 1.0f - MathF.Pow( t - 1.0f, 2.0f ) ),
			AnimGraphBuilder.Easing.InOutCirc => t < 0.5f
				? ( 1.0f - MathF.Sqrt( 1.0f - MathF.Pow( 2.0f * t, 2.0f ) ) ) / 2.0f
				: ( MathF.Sqrt( 1.0f - MathF.Pow( ( -2.0f * t ) + 2.0f, 2.0f ) ) + 1.0f ) / 2.0f,

			_ => t,
		};
	}
}
