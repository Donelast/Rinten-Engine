using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Makes an IK target from a position; a zero point means no target</summary>
[Title( "Point Target" ), Group( "Targets" ), Icon( "my_location" )]
public class PointTargetNode : Node
{
	//Not Position: a node already has one of those, and it is where the node sits on the canvas - an input of that name is saved over the layout.
	[Input( typeof( VectorLink ) )]
	public NodeInput Point { get; set; }

	[Output( typeof( TargetLink ) )]
	[JsonIgnore]
	public object Target => default;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var input = compiler.EmitValue( this, Point );

		if ( input is null )
		{
			compiler.Warn( "Point Target has no Point connected", this );
			return compiler.Builder.NullTarget();
		}

		return compiler.Builder.PointTarget( input );
	}
}
