using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Outputs identity transforms for every bone; not the bind pose (see Reference Pose)</summary>
[Title( "Zero Pose" ), Group( "Animation" ), Icon( "accessibility_new" )]
public class ZeroPoseNode : Node
{
	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler ) => compiler.Builder.ZeroPose();
}
