using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>The additive difference from Base to Pose; feed it into a layer's additive input</summary>
[Title( "Pose Difference" ), Group( "Poses" ), Icon( "difference" )]
public class PoseDifferenceNode : Node
{
	/// <summary>The pose being measured.</summary>
	[Input( typeof( PoseLink ) )]
	public NodeInput Pose { get; set; }

	/// <summary>What it is measured against.</summary>
	[Input( typeof( PoseLink ) )]
	public NodeInput Base { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#e5c07b" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var pose = compiler.EmitPose( this, Pose, "Pose Difference" );
		var reference = compiler.EmitPose( this, Base, "Pose Difference" );

		return compiler.Builder.PoseDifference( pose, reference );
	}
}
