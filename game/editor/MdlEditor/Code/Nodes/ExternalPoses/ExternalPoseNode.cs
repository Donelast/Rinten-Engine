using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Outputs pose samples supplied by game code through a named runtime slot</summary>
[Title( "External Pose" ), Group( "Animation" ), Icon( "input" )]
[AvailableIn( GraphKind.BlendTree )]
public class ExternalPoseNode : Node
{
	/// <summary>Slot id game code uses to set the pose. Must be unique.</summary>
	[Property]
	public string SlotName { get; set; } = "External Pose";

	[Property]
	public bool ShouldSampleRootMotion { get; set; } = false;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = DisplayInfo.For( this );
			if ( !string.IsNullOrWhiteSpace( SlotName ) )
				info.Name = $"External Pose: {SlotName}";
			return info;
		}
	}

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		return compiler.RegisterExternalPoseSlot( SlotName, this )
			? compiler.Builder.ExternalPose( SlotName, ShouldSampleRootMotion )
			: compiler.ReferencePose();
	}
}
