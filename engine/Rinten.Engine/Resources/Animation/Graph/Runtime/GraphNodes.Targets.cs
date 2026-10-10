using System.Text.Json.Nodes;

using Rinten.CoreAnimation;

namespace Rinten.Animation;

//Target nodes: a transform that may also be unset, which is kept distinct from the origin
internal sealed class BoneTargetNode : GraphNode
{
	private readonly string bone;

	public BoneTargetNode( JsonObject node ) => bone = GraphFields.Text( node, "Bone" );

	private bool Read( GraphContext context, out Transform where )
	{
		where = Transform.Zero;

		var index = context.FindBone( bone );

		if ( index < 0 )
		{
			Owner.MissingBone( bone );
			return false;
		}

		if ( !context.Instance.ModelBone( index, out var trs ) ) return false;

		where = trs.ToTransform();

		return true;
	}

	protected override Transform OnTarget( GraphContext context ) => Read( context, out var where ) ? where : Transform.Zero;

	public override bool HasTarget( GraphContext context ) => Read( context, out _ );

	/// <summary>Reads the bone from the pose being built, not the standing pose, so IK isn't a frame late</summary>
	public bool ReadFrom( GraphContext context, int pose, out Transform where )
	{
		where = Transform.Zero;

		if ( pose < 0 ) return Read( context, out where );

		var live = context.Instance;

		var local = live?.Pose( pose );
		if ( local is null ) return Read( context, out where );

		var index = context.FindBone( bone );

		if ( index < 0 || index >= local.Length )
		{
			Owner.MissingBone( bone );
			return false;
		}

		var found = Rinten.CoreAnimation.CoreAnimRuntime.ModelOf( local, live.Skeleton, index );

		where = new Transform( found.Position, found.Rotation, found.Scale.x );

		return true;
	}
}

//A target at a point something else worked out. What their graphs aim a hand or a foot with: the game writes a place into a vector
//parameter and the IK follows it. Nothing written is the origin, and the origin is read as nothing rather than as the character's own feet.
internal sealed class PointTargetNode : GraphNode
{
	private readonly int position;

	private GraphNode input;

	public PointTargetNode( JsonObject node ) => position = GraphFields.Reference( node, "Position" );

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, position );

	protected override Transform OnTarget( GraphContext context )
		=> input is null ? Transform.Zero : new Transform( input.Vector( context ) );

	public override bool HasTarget( GraphContext context )
		=> input is not null && input.Vector( context ).LengthSquared > 1e-8f;
}

//Whether a target has anything in it.
internal sealed class IsTargetSetNode : GraphNode
{
	private readonly int target;
	private GraphNode input;

	public IsTargetSetNode( JsonObject node ) => target = GraphFields.Reference( node, "Target" );

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, target );

	protected override bool OnBool( GraphContext context ) => input?.HasTarget( context ) ?? false;

	protected override float OnFloat( GraphContext context ) => OnBool( context ) ? 1.0f : 0.0f;
}

//A number about a target: how far away it is, or which way it lies. What turns "there is something over there" into the
//parameters a blend space is driven by. Measured from where the model is, because the model is what is being turned towards it.
internal sealed class TargetInfoNode : GraphNode
{
	private readonly int target;
	private readonly AnimGraphBuilder.TargetInfoType info;
	private readonly bool worldSpace;

	private GraphNode input;

	public TargetInfoNode( JsonObject node )
	{
		target = GraphFields.Reference( node, "Target" );
		info = GraphFields.Enum( node, "Info", AnimGraphBuilder.TargetInfoType.Distance );
		worldSpace = GraphFields.Flag( node, "WorldSpace" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, target );

	protected override float OnFloat( GraphContext context )
	{
		if ( input is null || !input.HasTarget( context ) ) return 0.0f;

		var where = input.Target( context );

		//A target in the world is measured against where the model is standing; one already in the model's
		//space is measured from its origin, which is the same thing said in the space it is already in.
		if ( worldSpace ) where = context.ToModel( where );

		var offset = where.Position;

		return info switch
		{
			//The heading is about up, which is y, measured from the way the model faces, which is -z; the ground plane is x and z and the height is y.
			AnimGraphBuilder.TargetInfoType.AngleHorizontal
				=> MathF.Atan2( -offset.x, -offset.z ).RadianToDegree(),

			AnimGraphBuilder.TargetInfoType.AngleVertical
				=> MathF.Atan2( offset.y, new Vector2( offset.x, offset.z ).Length ).RadianToDegree(),

			AnimGraphBuilder.TargetInfoType.Distance => offset.Length,
			AnimGraphBuilder.TargetInfoType.DistanceHorizontalOnly => new Vector2( offset.x, offset.z ).Length,
			AnimGraphBuilder.TargetInfoType.DistanceVerticalOnly => MathF.Abs( offset.y ),

			AnimGraphBuilder.TargetInfoType.DeltaOrientationX => where.Rotation.Angles().pitch,
			AnimGraphBuilder.TargetInfoType.DeltaOrientationY => where.Rotation.Angles().yaw,
			AnimGraphBuilder.TargetInfoType.DeltaOrientationZ => where.Rotation.Angles().roll,

			_ => 0.0f,
		};
	}
}

//Where a target is, as a direction to feed something else.
internal sealed class TargetPointNode : GraphNode
{
	private readonly int target;
	private readonly bool worldSpace;

	private GraphNode input;

	public TargetPointNode( JsonObject node )
	{
		target = GraphFields.Reference( node, "Target" );
		worldSpace = GraphFields.Flag( node, "WorldSpace" );
	}

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, target );

	protected override Vector3 OnVector( GraphContext context )
	{
		if ( input is null || !input.HasTarget( context ) ) return Vector3.Zero;

		var where = input.Target( context );

		return worldSpace ? context.ToModel( where ).Position : where.Position;
	}
}

//A target moved a little from where it is - a hand held a hand's width off a ledge rather than inside it.
internal sealed class TargetOffsetNode : GraphNode
{
	private readonly int target;
	private readonly Vector3 position;
	private readonly Rotation rotation;
	private readonly bool boneSpace;

	private GraphNode input;

	public TargetOffsetNode( JsonObject node )
	{
		target = GraphFields.Reference( node, "Target" );
		boneSpace = GraphFields.Flag( node, "BoneSpace" );

		var offset = GraphFields.Array( node, "Position" );
		var turn = GraphFields.Array( node, "Rotation" );

		position = offset is null
			? Vector3.Zero
			: new Vector3( Read( offset, 0 ), Read( offset, 1 ), Read( offset, 2 ) );

		rotation = turn is null
			? Rotation.Identity
			: new Rotation( Read( turn, 0 ), Read( turn, 1 ), Read( turn, 2 ), Read( turn, 3 ) );
	}

	private static float Read( JsonArray array, int index )
		=> index < array.Count && array[index] is { } value ? value.GetValue<float>() : 0.0f;

	public override void Link( GraphNode[] nodes ) => input = ValueNodes.Get( nodes, target );

	protected override Transform OnTarget( GraphContext context )
	{
		if ( input is null || !input.HasTarget( context ) ) return Transform.Zero;

		var where = input.Target( context );

		//In the target's own space the offset comes along when it turns, which is what "a hand's width
		//in front of the ledge" means. Otherwise it is a shift in the space the target is already in.
		return boneSpace
			? new Transform( where.Position + ( position * where.Rotation ), where.Rotation * rotation, where.Scale )
			: new Transform( where.Position + position, where.Rotation * rotation, where.Scale );
	}

	public override bool HasTarget( GraphContext context ) => input?.HasTarget( context ) ?? false;
}

//Whichever of several targets is nearest what is wanted. A character asked to grab a ledge is really asked to grab one of the places a ledge can
//be grabbed. Nearest is by position and by which way it faces, weighted, because a handhold facing the wrong way is further away than it looks.
internal sealed class TargetSelectNode : GraphNode
{
	private readonly int target;
	private readonly int[] options;
	private readonly float positionWeight, orientationWeight;
	private readonly bool worldSpace, ignoreInvalid;

	private GraphNode input;
	private GraphNode[] choices = Array.Empty<GraphNode>();

	public TargetSelectNode( JsonObject node )
	{
		target = GraphFields.Reference( node, "Target" );

		positionWeight = GraphFields.Number( node, "PositionWeight", 1.0f );
		orientationWeight = GraphFields.Number( node, "OrientationWeight" );

		worldSpace = GraphFields.Flag( node, "WorldSpace" );
		ignoreInvalid = GraphFields.Flag( node, "IgnoreInvalid" );

		var list = GraphFields.Array( node, "Options" );
		options = new int[list?.Count ?? 0];

		for ( var i = 0; i < options.Length; i++ ) options[i] = list[i]?.GetValue<int>() ?? -1;
	}

	public override void Link( GraphNode[] nodes )
	{
		input = ValueNodes.Get( nodes, target );

		choices = new GraphNode[options.Length];
		for ( var i = 0; i < options.Length; i++ ) choices[i] = ValueNodes.Get( nodes, options[i] );
	}

	private GraphNode Nearest( GraphContext context )
	{
		if ( input is null || !input.HasTarget( context ) ) return null;

		var wanted = input.Target( context );
		if ( worldSpace ) wanted = context.ToModel( wanted );

		GraphNode best = null;
		var bestScore = float.MaxValue;

		foreach ( var option in choices )
		{
			if ( option is null ) continue;

			if ( !option.HasTarget( context ) )
			{
				//An option that is not set is either skipped or is the answer that nothing was found, which is what the graph asked for.
				if ( ignoreInvalid ) continue;

				return null;
			}

			var candidate = option.Target( context );
			if ( worldSpace ) candidate = context.ToModel( candidate );

			var score = ( candidate.Position.Distance( wanted.Position ) * positionWeight )
				+ ( Rotation.Difference( candidate.Rotation, wanted.Rotation ).Angle() * orientationWeight );

			if ( score >= bestScore ) continue;

			bestScore = score;
			best = option;
		}

		return best;
	}

	protected override Transform OnTarget( GraphContext context )
		=> Nearest( context ) is { } best ? best.Target( context ) : Transform.Zero;

	public override bool HasTarget( GraphContext context ) => Nearest( context ) is not null;
}

//Picks a weighted variation by a number on entry and holds it until re-entered
internal sealed class ParameterSelectNode : PoseNode
{
	private readonly int parameter;
	private readonly int[] options;
	private readonly int[] weights;
	private readonly bool hasWeights, ignoreInvalid;

	private GraphNode input;
	private GraphNode[] poses = Array.Empty<GraphNode>();

	private int chosen = -1;

	public ParameterSelectNode( JsonObject node )
	{
		parameter = GraphFields.Reference( node, "Parameter" );

		hasWeights = GraphFields.Flag( node, "HasWeights" );
		ignoreInvalid = GraphFields.Flag( node, "IgnoreInvalid" );

		var list = GraphFields.Array( node, "Options" );
		options = new int[list?.Count ?? 0];

		for ( var i = 0; i < options.Length; i++ ) options[i] = list[i]?.GetValue<int>() ?? -1;

		var shares = GraphFields.Array( node, "Weights" );
		weights = new int[options.Length];

		for ( var i = 0; i < weights.Length; i++ )
		{
			var share = shares is not null && i < shares.Count ? (int)( shares[i]?.GetValue<float>() ?? 1.0f ) : 1;
			weights[i] = Math.Max( share, 0 );
		}
	}

	public override void Link( GraphNode[] nodes )
	{
		input = ValueNodes.Get( nodes, parameter );

		poses = new GraphNode[options.Length];
		for ( var i = 0; i < options.Length; i++ ) poses[i] = ValueNodes.Get( nodes, options[i] );
	}

	public override void Reset( GraphContext context )
	{
		chosen = -1;
		Untick();
	}

	protected override int OnPose( GraphContext context )
	{
		if ( chosen < 0 ) chosen = Pick( context );

		if ( chosen < 0 || chosen >= poses.Length || poses[chosen] is null ) return context.RestPose();

		return poses[chosen].Pose( context );
	}

	//Which option the value counts to.
	private int Pick( GraphContext context )
	{
		if ( poses.Length == 0 ) return -1;

		//The whole part of it, however far either side of zero: what picks is how many, not how much.
		var value = (int)MathF.Abs( MathF.Floor( input?.Float( context ) ?? 0.0f ) );

		var total = 0;

		for ( var i = 0; i < poses.Length; i++ )
		{
			//An option that failed to compile is nothing to pick, if the graph said to skip those
			//- otherwise it is a hole the count falls into, which is what "not ignoring" it means.
			if ( ignoreInvalid && poses[i] is null ) continue;

			total += hasWeights ? weights[i] : 1;
		}

		//Nothing to count through picks nothing, which is a pose of rest rather than the first option by accident.
		if ( total <= 0 ) return -1;

		var at = value % total;

		for ( var i = 0; i < poses.Length; i++ )
		{
			if ( ignoreInvalid && poses[i] is null ) continue;

			at -= hasWeights ? weights[i] : 1;
			if ( at < 0 ) return i;
		}

		return -1;
	}

	public override float Duration( GraphContext context )
		=> chosen >= 0 && chosen < poses.Length ? poses[chosen]?.Duration( context ) ?? 0.0f : 0.0f;

	public override float Progress( GraphContext context )
		=> chosen >= 0 && chosen < poses.Length ? poses[chosen]?.Progress( context ) ?? 0.0f : 0.0f;
}

//Blends clips placed by their own root speed against the wanted speed, keeping them in step
internal sealed class VelocityBlendNode : PoseNode
{
	private readonly int velocity;
	private readonly int[] options;

	private GraphNode input;
	private GraphNode[] poses = Array.Empty<GraphNode>();

	private float[] speeds;
	private int[] order;

	public VelocityBlendNode( JsonObject node )
	{
		velocity = GraphFields.Reference( node, "Velocity" );

		var list = GraphFields.Array( node, "Options" );
		options = new int[list?.Count ?? 0];

		for ( var i = 0; i < options.Length; i++ ) options[i] = list[i]?.GetValue<int>() ?? -1;
	}

	public override void Link( GraphNode[] nodes )
	{
		input = ValueNodes.Get( nodes, velocity );

		poses = new GraphNode[options.Length];
		for ( var i = 0; i < options.Length; i++ ) poses[i] = ValueNodes.Get( nodes, options[i] );
	}

	//The clips in the order their own speeds put them.
	private void Measure( GraphContext context )
	{
		if ( speeds is not null ) return;

		speeds = new float[poses.Length];
		order = new int[poses.Length];

		for ( var i = 0; i < poses.Length; i++ )
		{
			speeds[i] = poses[i]?.Speed( context ) ?? 0.0f;
			order[i] = i;
		}

		Array.Sort( order, ( a, b ) => speeds[a].CompareTo( speeds[b] ) );
	}

	protected override int OnPose( GraphContext context )
	{
		Measure( context );

		if ( poses.Length == 0 ) return context.RestPose();
		if ( poses.Length == 1 ) return poses[0]?.Pose( context ) ?? context.RestPose();

		var wanted = input?.Float( context ) ?? 0.0f;

		var first = order[0];
		var last = order[^1];

		//Slower than the slowest or faster than the fastest is that clip, played at its own speed.
		//Making it go faster is a job for something that scales time, and this is not that node.
		if ( wanted <= speeds[first] ) return poses[first]?.Pose( context ) ?? context.RestPose();
		if ( wanted >= speeds[last] ) return poses[last]?.Pose( context ) ?? context.RestPose();

		for ( var i = 0; i + 1 < order.Length; i++ )
		{
			var below = order[i];
			var above = order[i + 1];

			if ( wanted < speeds[below] || wanted > speeds[above] ) continue;

			var span = speeds[above] - speeds[below];
			var alpha = span <= 1e-6f ? 0.0f : ( wanted - speeds[below] ) / span;

			context.Debug?.Blend( this, poses[below]?.Index ?? -1, poses[above]?.Index ?? -1, alpha );

			if ( alpha <= 0.0f ) return poses[below]?.Pose( context ) ?? context.RestPose();
			if ( alpha >= 1.0f ) return poses[above]?.Pose( context ) ?? context.RestPose();

			var was = context.PushWeight( 1.0f - alpha );
			var slower = poses[below].Pose( context );
			context.PopWeight( was );

			//The faster clip is put where the slower one is before it plays: the same walk at two speeds is
			//the same movement, and the step it is about to take is taken off because it has not ticked yet.
			var length = poses[above].Duration( context );

			if ( length > 0.0f )
				poses[above].Seek( context, ( poses[below].Progress( context ) * length ) - context.Delta );

			was = context.PushWeight( alpha );
			var faster = poses[above].Pose( context );
			context.PopWeight( was );

			return context.Blend( slower, faster, alpha );
		}

		return poses[first]?.Pose( context ) ?? context.RestPose();
	}

	public override float Duration( GraphContext context )
	{
		var longest = 0.0f;
		foreach ( var pose in poses ) longest = MathF.Max( longest, pose?.Duration( context ) ?? 0.0f );

		return longest;
	}
}
