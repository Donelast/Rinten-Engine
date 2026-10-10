using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Crossfades between two poses on a bool, re-reading it every update</summary>
[Title( "Pose Switch" ), Group( "Poses" ), Icon( "swap_horiz" )]
public class PoseSwitchNode : Node
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Condition { get; set; }

	[Input( typeof( PoseLink ) )]
	public NodeInput WhenTrue { get; set; }

	[Input( typeof( PoseLink ) )]
	public NodeInput WhenFalse { get; set; }

	/// <summary>Seconds to cross from one pose to the other. Zero snaps.</summary>
	[Property]
	public float BlendTime { get; set; } = 0.2f;

	/// <summary>Whether the two poses play in step with each other while they cross.</summary>
	[Property]
	public bool SyncCycles { get; set; } = true;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#e5c07b" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var whenTrue = compiler.EmitPose( this, WhenTrue, "Pose Switch" );
		var whenFalse = compiler.EmitPose( this, WhenFalse, "Pose Switch" );

		var condition = compiler.EmitValue( this, Condition ) ?? compiler.ConstBool( false );

		//Nought when the flag is clear and one when it is set, eased across the blend time - which
		//is the crossfade, expressed as where along the axis the blend below is being asked for.
		var weight = compiler.Builder.Switch( condition, 1.0f, 0.0f );

		if ( BlendTime > 0.0f )
		{
			weight = compiler.Builder.Ease( weight,
				new AnimGraphBuilder.EaseSettings { Time = BlendTime } );
		}

		return compiler.Builder.BlendSpace(
			weight,
			[whenFalse, whenTrue],
			[(0, 1, 0.0f, 1.0f)],
			SyncCycles );
	}
}
