using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Replaces root motion with a desired velocity, facing direction, or both</summary>
[Title( "Root Motion Override" ), Group( "Root Motion" ), Icon( "alt_route" )]
public class RootMotionOverrideNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( VectorLink ) ), Title( "Desired Moving Velocity (Character)" )]
	public NodeInput DesiredMovingVelocity { get; set; }

	[Input( typeof( VectorLink ) ), Title( "Desired Facing Direction (Character)" )]
	public NodeInput DesiredFacingDirection { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Linear Velocity Limit (Optional)" )]
	public NodeInput LinearVelocityLimit { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Angular Velocity Limit (Optional)" )]
	public NodeInput AngularVelocityLimit { get; set; }

	/// <summary>Set to negative to disable the velocity limiter</summary>
	[Property]
	public float MaxLinearVelocity { get; set; } = -1.0f;

	/// <summary>Set to negative to disable the velocity limiter (degrees)</summary>
	[Property]
	public float MaxAngularVelocity { get; set; } = -1.0f;

	/// <summary>Allow movement along the x axis</summary>
	[Property]
	public bool OverrideMoveDirX { get; set; } = true;

	/// <summary>Allow movement along the y axis</summary>
	[Property]
	public bool OverrideMoveDirY { get; set; } = true;

	/// <summary>Allow movement along the z axis</summary>
	[Property]
	public bool OverrideMoveDirZ { get; set; } = true;

	/// <summary>Allow pitching the character facing (i.e. 3D facing)</summary>
	[Property]
	public bool AllowPitchForFacing { get; set; }

	/// <summary>Listen for root motion override events</summary>
	[Property]
	public bool ListenForRootMotionEvents { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Root Motion Override" );

		var movingVelocity = compiler.EmitValue( this, DesiredMovingVelocity );
		var facingDirection = compiler.EmitValue( this, DesiredFacingDirection );

		if ( movingVelocity is null && facingDirection is null )
		{
			compiler.Warn( "Root Motion Override needs at least one of the movement/facing inputs connected" );
			return child;
		}

		var flags = AnimGraphBuilder.RootMotionOverrideFlags.None;
		if ( OverrideMoveDirX ) flags |= AnimGraphBuilder.RootMotionOverrideFlags.MoveX;
		if ( OverrideMoveDirY ) flags |= AnimGraphBuilder.RootMotionOverrideFlags.MoveY;
		if ( OverrideMoveDirZ ) flags |= AnimGraphBuilder.RootMotionOverrideFlags.MoveZ;
		if ( AllowPitchForFacing ) flags |= AnimGraphBuilder.RootMotionOverrideFlags.FacingPitch;
		if ( ListenForRootMotionEvents ) flags |= AnimGraphBuilder.RootMotionOverrideFlags.ListenForEvents;

		var linearLimit = compiler.EmitValue( this, LinearVelocityLimit );
		var angularLimit = compiler.EmitValue( this, AngularVelocityLimit );
		var maxAngularVelocity = MaxAngularVelocity < 0.0f
			? MathX.DegreeToRadian( MaxAngularVelocity )
			: MaxAngularVelocity;

		return compiler.Builder.RootMotionOverride(
			child,
			new AnimGraphBuilder.RootMotionOverrideSettings
			{
				MovingVelocity = movingVelocity,
				FacingDirection = facingDirection,
				LinearVelocityLimit = linearLimit,
				AngularVelocityLimit = angularLimit,
				MaxLinearVelocity = MaxLinearVelocity,
				MaxAngularVelocityDegrees = maxAngularVelocity,
				Flags = flags,
			} );
	}
}
