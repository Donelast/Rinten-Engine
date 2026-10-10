using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Drives a pose's playback position from an input instead of its own clock</summary>
[Title( "Cycle Control" ), Group( "Poses" ), Icon( "tune" )]
public class CycleControlNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Pose { get; set; }

	/// <summary>How far through, from nothing to one.</summary>
	[Input( typeof( FloatLink ) )]
	public NodeInput Cycle { get; set; }

	/// <summary>Whether the value is a time in seconds rather than a fraction of the clip.</summary>
	[Property]
	public bool InSeconds { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Blue;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var pose = compiler.EmitPose( this, Pose, "Cycle Control" );
		var cycle = compiler.EmitValue( this, Cycle );

		//Nothing driving it is not "play normally": it is a scrub with no hand on it, and answering
		//that with the pose itself keeps the animation moving instead of freezing it at nought.
		if ( cycle is null )
		{
			compiler.Warn( "Cycle Control has nothing driving its cycle", this );
			return pose;
		}

		return compiler.Builder.CycleControl( pose, cycle, InSeconds );
	}
}
