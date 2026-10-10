using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Selects a model bone mask or a skeleton branch rooted at a named bone</summary>
[Title( "Bone Mask" ), Group( "Bone Mask" ), Icon( "filter_alt" )]
public class BoneMaskNode : Node
{
	/// <summary>Authored bone mask or root bone - a root bone covers itself and all descendants. Leave empty to mask the whole skeleton by weight.</summary>
	[Property, Title( "Mask" ), AnimGraphBoneMask]
	public string BoneName { get; set; } = "";

	/// <summary>Weight applied to every bone when no bone name is set.</summary>
	[Property, Range( 0, 1 )]
	public float DefaultWeight { get; set; } = 1.0f;

	[Output( typeof( BoneMaskLink ) )]
	[JsonIgnore]
	public object Mask => default;

	public override Color PrimaryColor => Color.Parse( "#d19a66" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		return string.IsNullOrWhiteSpace( BoneName )
			? compiler.Builder.BoneMask( DefaultWeight )
			: compiler.Builder.BoneMask( BoneName );
	}
}
