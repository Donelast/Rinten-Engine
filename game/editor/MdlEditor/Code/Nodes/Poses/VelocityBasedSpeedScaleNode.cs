using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Scales an Animation Clip's speed so its root-motion velocity matches a desired one</summary>
[Title( "Velocity Based Speed Scale" ), Group( "Animation" ), Icon( "directions_run" )]
public class VelocityBasedSpeedScaleNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Source { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Desired Velocity" )]
	public NodeInput DesiredVelocity { get; set; }

	/// <summary>Velocity used when the input is not connected. Must be positive.</summary>
	[Property]
	public float DefaultVelocity { get; set; } = 1.0f;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Source, "Velocity Based Speed Scale" );
		if ( !child.IsClip() )
		{
			compiler.Warn( "Velocity Based Speed Scale's input must be an Animation Clip" );
			return child;
		}

		var input = compiler.EmitValue( this, DesiredVelocity );
		if ( input is null && DefaultVelocity <= 0.0f )
		{
			compiler.Warn( "Velocity Based Speed Scale requires a positive default velocity when the input is not connected" );
			return child;
		}

		return compiler.Builder.VelocityScale(
			 child,
			 input,
			DefaultVelocity );
	}
}
