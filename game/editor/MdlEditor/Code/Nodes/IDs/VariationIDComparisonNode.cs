using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>True if the input ID is in a per-variation ID list (or not, for Doesn't Match)</summary>
[Title( "Variation ID Comparison" ), Group( "Values" ), Icon( "rule" )]
public class VariationIDComparisonNode : VariationDataNode<VariationIDComparisonNode.Data>
{
	/// <summary>The parts of a variation ID comparison node that can differ per variation.</summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property]
		public List<string> Ids { get; set; } = [];
	}

	[Input( typeof( IDLink ) )]
	public NodeInput Id { get; set; }

	[Property]
	public IDComparisonNode.ComparisonType Comparison { get; set; } = IDComparisonNode.ComparisonType.Matches;

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#c678dd" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Id );
		if ( input is null )
		{
			compiler.Warn( "Variation ID Comparison has no Id connected" );
			return compiler.ConstBool( false );
		}

		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );
		return compiler.Builder.IdCompare(
			input, data.Ids ?? [], Comparison == IDComparisonNode.ComparisonType.Matches );
	}

	internal override void BuildDebugWidgets( Rinten.UI.Panel parent )
	{
		base.BuildDebugWidgets( parent );

		IDComparisonNode.BuildIdList( parent, Comparison, ((Data)SelectedVariationData).Ids );
	}
}
