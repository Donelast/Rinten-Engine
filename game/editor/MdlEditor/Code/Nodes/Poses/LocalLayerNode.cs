using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>A local layer for Layer Blend: pose, blend mode, weights and bone mask</summary>
[Title( "Local Layer" ), Group( "Layers" ), Icon( "layers" )]
public class LocalLayerNode : Node
{
	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( FloatLink ) )]
	public NodeInput Weight { get; set; }

	[Input( typeof( FloatLink ) ), Title( "Root Motion Weight" )]
	public NodeInput RootMotionWeight { get; set; }

	[Input( typeof( BoneMaskLink ) ), Title( "Bone Mask" )]
	public NodeInput BoneMask { get; set; }

	[Property]
	public AnimGraphBuilder.LayerBlendMode BlendMode { get; set; } = AnimGraphBuilder.LayerBlendMode.Overlay;

	//What a mask of part-weights means: a hand at one and the spine under it at a quarter puts the hand exactly where the layer says while the spine leans a quarter of the way. Blended under each parent instead, the shares compound down the chain and the hand overshoots by everything its parents did.
	/// <summary>Blend where the bones stand rather than where their parents put them.</summary>
	[Property, Title( "Blend In Model Space" )]
	public bool BlendInModelSpace { get; set; }

	/// <summary>Take only which way bones point, leaving their offsets alone.</summary>
	[Property]
	public bool RotationOnly { get; set; }

	/// <summary>Lay a difference on a channel at a time - offsets added, turns composed.</summary>
	[Property]
	public bool ChannelsApart { get; set; }

	[Property]
	public bool IsSynchronized { get; set; }

	[Property]
	public bool IgnoreEvents { get; set; }

	[Output( typeof( LayerLink ) )]
	[JsonIgnore]
	public object Layer => default;

	public override Color PrimaryColor => Color.Parse( "#ff6347" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		compiler.Warn( "Local Layer must be connected to a Layer Blend node", this );
		return compiler.ReferencePose();
	}
}
