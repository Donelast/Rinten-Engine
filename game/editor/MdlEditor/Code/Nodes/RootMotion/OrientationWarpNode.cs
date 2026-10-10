using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Redirects a clip's root-motion facing toward a direction or by an angle offset</summary>
[Title( "Orientation Warp" ), Group( "Root Motion" ), Icon( "explore" )]
public class OrientationWarpNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( VectorLink ) ), Title( "Direction (Character)" )]
	public NodeInput Direction { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Angle Offset (Deg)" )]
	public NodeInput AngleOffset { get; set; }

	public enum OffsetType
	{
		[Title( "Relative To Character" )] RelativeToCharacter,
		[Title( "Relative To Original Root Motion" )] RelativeToOriginalRootMotion,
	}

	/// <summary>The coordinate space of the supplied offset</summary>
	[Property]
	public OffsetType Offset { get; set; } = OffsetType.RelativeToCharacter;

	/// <summary>The sampling mode for the warped motion</summary>
	[Property]
	public AnimGraphBuilder.RootMotionSamplingMode SamplingMode { get; set; } = AnimGraphBuilder.RootMotionSamplingMode.WorldSpace;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Orientation Warp" );

		if ( !child.IsClip() )
		{
			compiler.Warn( "Orientation Warp input must be an animation clip" );
			return child;
		}

		var direction = compiler.EmitValue( this, Direction );
		var offset = compiler.EmitValue( this, AngleOffset );

		if ( direction is not null && offset is not null )
		{
			compiler.Warn( "Orientation Warp cannot have both the direction and the offset inputs set, these are mutually exclusive" );
			return child;
		}

		var target = direction ?? offset;
		if ( target is null )
		{
			compiler.Warn( "Orientation Warp needs either the direction or the offset input connected" );
			return child;
		}

		return compiler.Builder.OrientationWarp(
			 child, target,
			Offset == OffsetType.RelativeToCharacter,
			SamplingMode );
	}
}
