using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Warps a clip's root motion to reach a world target using its target-warp events</summary>
[Title( "Target Warp" ), Group( "Root Motion" ), Icon( "ads_click" )]
public class TargetWarpNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( TargetLink ) ), Title( "World Target" )]
	public NodeInput WorldTarget { get; set; }

	/// <summary>Can this warp be updated when the target changes?</summary>
	[Property]
	public AnimGraphBuilder.TargetWarpUpdate UpdateRule { get; set; } = AnimGraphBuilder.TargetWarpUpdate.None;

	/// <summary>Do we want to be aligned with the warp target at the end of the last warp event?</summary>
	[Property]
	public bool AlignWithTargetAtLastWarpEvent { get; set; }

	/// <summary>The sampling mode for the warped motion</summary>
	[Property]
	public AnimGraphBuilder.RootMotionSamplingMode SamplingMode { get; set; } = AnimGraphBuilder.RootMotionSamplingMode.WorldSpace;

	/// <summary>The error threshold to exceed, when accurately sampling, before switching to inaccurate sampling</summary>
	[Property]
	public float SamplingPositionErrorThreshold { get; set; } = 0.05f;

	/// <summary>Length limit on generated warp curve tangents. The lower the length the lower the curvature.</summary>
	[Property]
	public float MaxTangentLength { get; set; } = 1.25f;

	/// <summary>The distance under which we fall back to lerping the translation instead of generating a curve</summary>
	[Property]
	public float LerpFallbackDistanceThreshold { get; set; } = 0.1f;

	/// <summary>The distance the target needs to move from the original target before we update the warp</summary>
	[Property]
	public float TargetUpdateDistanceThreshold { get; set; } = 0.1f;

	/// <summary>The angle (degrees) the target needs to rotate from the original target before we update the warp</summary>
	[Property]
	public float TargetUpdateAngleThreshold { get; set; } = 5.0f;

	/// <summary>Which bone do we want to align with the target</summary>
	[Property, AnimGraphBone]
	public string AlignmentBone { get; set; } = "";

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Target Warp" );

		if ( !child.IsClip() )
		{
			compiler.Warn( "Target Warp input must be an animation clip" );
			return child;
		}

		var target = compiler.EmitValue( this, WorldTarget );
		if ( target is null )
		{
			compiler.Warn( "Target Warp has no world target connected" );
			return child;
		}

		return compiler.Builder.TargetWarp(
			child,
			target,
			new AnimGraphBuilder.TargetWarpSettings
			{
				UpdateRule = UpdateRule,
				SamplingMode = SamplingMode,
				AlignAtLastWarpEvent = AlignWithTargetAtLastWarpEvent,
				AlignmentBone = AlignmentBone,
				SamplingPositionErrorThreshold = SamplingPositionErrorThreshold,
				MaxTangentLength = MaxTangentLength,
				LerpFallbackDistance = LerpFallbackDistanceThreshold,
				TargetUpdateDistance = TargetUpdateDistanceThreshold,
				TargetUpdateAngleDegrees = TargetUpdateAngleThreshold,
			} );
	}
}
