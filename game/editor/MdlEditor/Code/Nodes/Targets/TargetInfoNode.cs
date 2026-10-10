using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Extracts angle, distance or orientation of a target relative to the character</summary>
[Title( "Target Info" ), Group( "Values" ), Icon( "info" )]
public class TargetInfoNode : Node
{
	[Input( typeof( TargetLink ) )]
	public NodeInput Target { get; set; }

	[Property]
	public AnimGraphBuilder.TargetInfoType Info { get; set; } = AnimGraphBuilder.TargetInfoType.Distance;

	[Property]
	public bool IsWorldSpaceTarget { get; set; } = true;

	[Output( typeof( FloatLink ) )]
	[JsonIgnore]
	public object Result => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Target );
		if ( input is null )
		{
			compiler.Warn( "Target Info has no Target connected" );
			return compiler.ConstFloat( 0.0f );
		}

		return compiler.Builder.TargetInfo( input, Info, IsWorldSpaceTarget );
	}
}
