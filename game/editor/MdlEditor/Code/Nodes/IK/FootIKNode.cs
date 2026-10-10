using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>Two-bone IK for left and right feet toward separate targets, with blending</summary>
[Title( "Foot IK" ), Group( "IK" ), Icon( "do_not_step" )]
public class FootIKNode : VariationDataNode<FootIKNode.Data>
{
	/// <summary>The parts of a foot IK node that can differ per variation.</summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		/// <summary>The limb the model names for the left leg.</summary>
		[Property, AnimGraphIkChain]
		public string LeftFootChain { get; set; } = "";

		/// <summary>The limb the model names for the right leg.</summary>
		[Property, AnimGraphIkChain]
		public string RightFootChain { get; set; } = "";

		/// <summary>The left foot bone, where no chain is named.</summary>
		[Property, AnimGraphBone]
		public string LeftFootBone { get; set; } = "";

		/// <summary>The right foot bone, where no chain is named.</summary>
		[Property, AnimGraphBone]
		public string RightFootBone { get; set; } = "";

		/// <summary>The amount of time to blend IK in or out.</summary>
		[Property]
		public float BlendTime { get; set; }

		public bool HasLeft => !string.IsNullOrWhiteSpace( LeftFootChain ) || !string.IsNullOrWhiteSpace( LeftFootBone );
		public bool HasRight => !string.IsNullOrWhiteSpace( RightFootChain ) || !string.IsNullOrWhiteSpace( RightFootBone );

		public override VisualState GetVisualState()
			=> HasLeft && HasRight ? VisualState.None : VisualState.HasUnsetData;
	}

	[Input( typeof( PoseLink ) )]
	public NodeInput Input { get; set; }

	[Input( typeof( TargetLink ) ), Title( "Left Foot Target" )]
	public NodeInput LeftFootTarget { get; set; }

	[Input( typeof( TargetLink ) ), Title( "Right Foot Target" )]
	public NodeInput RightFootTarget { get; set; }

	[Input( typeof( BoolLink ) )]
	public NodeInput Enable { get; set; }

	[Property]
	public AnimGraphBuilder.IKBlendMode BlendMode { get; set; } = AnimGraphBuilder.IKBlendMode.Effector;

	[Property]
	public bool IsTargetInWorldSpace { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#56b6c2" ).Value;

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		var child = compiler.EmitPose( this, Input, "Foot IK" );
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( !data.HasLeft || !data.HasRight )
		{
			compiler.Warn( "Foot IK has no feet set", this );
			return child;
		}

		//A chain the model does not name reaches for nothing at runtime, so a rename
		//on the model shows up here rather than as a foot that stopped being planted.
		if ( compiler.TargetModel is { IsValid: true } model )
		{
			foreach ( var chain in new[] { data.LeftFootChain, data.RightFootChain } )
			{
				if ( string.IsNullOrWhiteSpace( chain ) || IkChainCatalog.Has( model, chain ) ) continue;

				compiler.Warn( $"Foot IK names ik chain '{chain}', which this model does not have", this );
			}
		}

		var left = compiler.EmitValue( this, LeftFootTarget );
		var right = compiler.EmitValue( this, RightFootTarget );

		if ( left is null && right is null )
		{
			compiler.Warn( "Foot IK has no foot targets connected", this );
			return child;
		}

		var enabled = compiler.EmitValue( this, Enable );
		return compiler.Builder.FootIK(
			child,
			data.LeftFootBone,
			data.RightFootBone,
			new AnimGraphBuilder.FootIKSettings
			{
				LeftChain = data.LeftFootChain,
				RightChain = data.RightFootChain,
				LeftTarget = left,
				RightTarget = right,
				Enabled = enabled,
				BlendTime = MathF.Max( data.BlendTime, 0 ),
				BlendMode = BlendMode,
				WorldSpace = IsTargetInWorldSpace,
			} );
	}
}
