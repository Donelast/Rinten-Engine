using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Returns true while a named External Pose slot contains valid pose data. <br/><br/><b>Use when:</b>&#160;Selecting behavior based on whether game code has supplied an external pose. <br/><b>Important:</b>&#160;If no matching External Pose node exists, the node warns and returns false.</summary>
[Title( "Is External Pose Filled" ), Group( "Values" ), Icon( "check_circle" )]
public class IsExternalPoseSetNode : Node
{
	/// <summary>Slot name of the External Pose node to check.</summary>
	[Property]
	public string SlotName { get; set; } = "";

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var slotNode = Root?.AllNodes.OfType<ExternalPoseNode>().FirstOrDefault( n => n.SlotName == SlotName );
		if ( slotNode is null )
		{
			compiler.Warn( $"No External Pose node with slot name '{SlotName}'", this );
			return compiler.ConstBool( false );
		}

		var slot = compiler.Emit( slotNode );
		return compiler.Builder.IsExternalPoseSet( slot );
	}
}
