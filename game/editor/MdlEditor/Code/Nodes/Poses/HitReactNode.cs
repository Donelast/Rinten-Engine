using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Shoves a hit bone and its parents on trigger, then springs them back</summary>
[Title( "Hit React" ), Group( "Poses" ), Icon( "bolt" )]
public class HitReactNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Pose { get; set; }

	/// <summary>That a blow landed at all.</summary>
	[Input( typeof( BoolLink ) )]
	public NodeInput Trigger { get; set; }

	/// <summary>Which bone was struck.</summary>
	[Input( typeof( IDLink ) )]
	public NodeInput HitBone { get; set; }

	/// <summary>Where on that bone, in its own space.</summary>
	[Input( typeof( VectorLink ) )]
	public NodeInput HitOffset { get; set; }

	/// <summary>Which way the blow was going.</summary>
	[Input( typeof( VectorLink ) )]
	public NodeInput HitDirection { get; set; }

	/// <summary>How hard, as a fraction of the hardest.</summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput HitStrength { get; set; }

	/// <summary>How far up the chain the blow is felt, counting the bone hit.</summary>
	[Property]
	public int BoneCount { get; set; } = 3;

	[Property]
	public float MinImpactForce { get; set; } = 50.0f;

	[Property]
	public float MaxImpactForce { get; set; } = 250.0f;

	/// <summary>How quickly the bones come back.</summary>
	[Property]
	public float SpringStrength { get; set; } = 10.0f;

	/// <summary>How much the bone below turns the other way to take the blow.</summary>
	[Property]
	public float CounterRotationScale { get; set; } = 0.6f;

	/// <summary>What is left of the blow one bone further up.</summary>
	[Property]
	public float PropagationScale { get; set; } = 1.0f;

	/// <summary>The least time between two blows being felt.</summary>
	[Property]
	public float MinDelayBetweenHits { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#e06c75" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var pose = compiler.EmitPose( this, Pose, "Hit React" );

		var trigger = compiler.EmitValue( this, Trigger );
		var bone = compiler.EmitValue( this, HitBone );
		var direction = compiler.EmitValue( this, HitDirection );

		//Which bone, which way, and that it happened at all. Without those three there is no blow to
		//react to, and a node that reacted to one it could not place would shove whatever bone came first.
		if ( trigger is null || bone is null || direction is null )
		{
			compiler.Warn( "Hit React needs a trigger, a bone and a direction", this );
			return pose;
		}

		return compiler.Builder.HitReact( pose, trigger, bone,
			compiler.EmitValue( this, HitOffset ),
			direction,
			compiler.EmitValue( this, HitStrength ),
			new AnimGraphBuilder.HitReactSettings
			{
				BoneCount = BoneCount,
				MinImpactForce = MinImpactForce,
				MaxImpactForce = MaxImpactForce,
				SpringStrength = SpringStrength,
				CounterRotationScale = CounterRotationScale,
				PropagationScale = PropagationScale,
				MinDelayBetweenHits = MinDelayBetweenHits,
			} );
	}
}
