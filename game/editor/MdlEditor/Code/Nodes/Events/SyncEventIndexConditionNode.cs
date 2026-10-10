using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Compares the source state's sync-event index to a value; conduit transitions only</summary>
[Title( "Sync Event Index Condition" ), Group( "Events" ), Icon( "sync" )]
public class SyncEventIndexConditionNode : Node
{
	[Property]
	public AnimGraphBuilder.SyncEventTrigger Mode { get; set; } = AnimGraphBuilder.SyncEventTrigger.ExactlyAtEventIndex;

	[Property]
	public int SyncEventIndex { get; set; } = -1;

	[Output( typeof( BoolLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		if ( compiler.SourceState is null )
		{
			compiler.Warn( "Sync Event Index Condition is only usable in a conduit's transition graph" );
			return compiler.ConstBool( false );
		}

		if ( SyncEventIndex < 0 )
		{
			compiler.Warn( "Sync Event Index Condition has an invalid sync event index" );
			return compiler.ConstBool( false );
		}

		return compiler.Builder.SyncEventCondition(
			compiler.SourceState, SyncEventIndex, Mode );
	}
}
