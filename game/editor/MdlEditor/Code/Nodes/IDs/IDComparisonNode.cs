using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>True if the input ID is in the list (or not, for Doesn't Match)</summary>
[Title( "ID Comparison" ), Group( "Values" ), Icon( "rule" )]
public class IDComparisonNode : Node
{
	public enum ComparisonType
	{
		Matches,
		[Title( "Doesn't Match" )] DoesntMatch,
	}

	[Input( typeof( IDLink ) )]
	public NodeInput Id { get; set; }

	[Property]
	public ComparisonType Comparison { get; set; } = ComparisonType.Matches;

	[Property]
	public List<string> Ids { get; set; } = [];

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Id );
		if ( input is null )
		{
			compiler.Warn( "ID Comparison has no Id connected" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.IdCompare(
			 input, Ids ?? [], Comparison == ComparisonType.Matches );
	}

	internal override void BuildDebugWidgets( Rinten.UI.Panel parent )
	{
		base.BuildDebugWidgets( parent );

		BuildIdList( parent, Comparison, Ids );
	}

	//The list of IDs a comparison is against, under a line saying whether it wants one of them or none of them. Shared with
	//VariationIDComparisonNode, which asks the same question of a variation's own IDs and used to say so in its own copy of this.
	internal static void BuildIdList( Rinten.UI.Panel parent, ComparisonType comparison, IEnumerable<string> ids )
	{
		var heading = parent.AddChild<Rinten.UI.Label>( "debug-line" );
		heading.Text = comparison == ComparisonType.Matches ? "One of these:" : "NOT any of these:";

		foreach ( var id in ids ?? [] )
		{
			var line = parent.AddChild<Rinten.UI.Label>( "debug-line" );
			line.Text = $"\u2022 {(string.IsNullOrWhiteSpace( id ) ? "INVALID ID" : id)}";
		}
	}
}
