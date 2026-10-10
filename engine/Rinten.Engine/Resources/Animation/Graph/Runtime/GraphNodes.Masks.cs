using System.Text.Json.Nodes;

namespace Rinten.Animation;

//Bone masks: per-bone blend weight. Like poses, returned to the caller to free
internal sealed class BoneMaskNode : GraphNode
{
	private readonly string bone;

	//Weights by bone (parents first) for masks carried in the graph, e.g. ported named lists
	private readonly (string Bone, float Weight)[] weights;

	public BoneMaskNode( JsonObject node )
	{
		bone = GraphFields.Text( node, "Name" );

		var listed = GraphFields.Array( node, "Weights" );
		if ( listed is null ) return;

		var read = new List<(string, float)>( listed.Count );

		foreach ( var entry in listed )
		{
			if ( entry is not JsonObject o ) continue;

			var named = GraphFields.Text( o, "Bone" );
			if ( string.IsNullOrEmpty( named ) ) continue;

			read.Add( (named, GraphFields.Number( o, "Weight", 1.0f )) );
		}

		weights = read.ToArray();
	}

	protected override int OnMask( GraphContext context )
	{
		var mask = context.AllocMask();
		if ( mask < 0 ) return mask;

		//Written-down weights win: a mask that has them names a list rather than a bone, and looking its name up in the skeleton finds nothing.
		if ( weights is not null )
		{
			//Everything out, then each entry back in from its bone downwards - which is how the
			//weights were written: only where the value changes, inherited the rest of the way down.
			context.FillMask( mask, 0.0f );

			foreach ( var (named, weight) in weights )
			{
				var at = context.FindBone( named );

				//Not reported. A mask lists bones for every body a character ships with, and one
				//built for the tall skeleton naming a bone the short one does not have is ordinary.
				if ( at < 0 ) continue;

				context.SetMaskBone( mask, at, weight, true );
			}

			return mask;
		}

		//A name is a weight list on the model before it is a bone: that is what the picker offers, and what a mask authored on the model is called.
		if ( CoreSkeletons.Weights( context.Model, bone ) is { } listed )
		{
			context.FillMask( mask, listed.BaseWeight );

			foreach ( var (at, weight, descendants) in listed.Bones )
				context.SetMaskBone( mask, at, weight, descendants );

			return mask;
		}

		var index = context.FindBone( bone );

		if ( index < 0 )
		{
			//A mask naming a bone the model does not have would otherwise be a layer that silently applies to everything.
			context.FillMask( mask, 0.0f );
			Owner.MissingBone( bone );

			return mask;
		}

		//The named bone and everything hanging off it, which is what an artist means by "the upper body".
		context.FillMask( mask, 0.0f );
		context.SetMaskBone( mask, index, 1.0f, true );

		return mask;
	}
}

//The same weight on every bone.
internal sealed class BoneMaskWeightNode : GraphNode
{
	private readonly float weight;

	public BoneMaskWeightNode( JsonObject node ) => weight = GraphFields.Number( node, "Weight", 1.0f );

	protected override int OnMask( GraphContext context )
	{
		var mask = context.AllocMask();
		if ( mask < 0 ) return mask;

		context.FillMask( mask, weight );

		return mask;
	}
}

internal sealed class BoneMaskBlendNode : GraphNode
{
	private readonly int a, b, weight;

	private GraphNode first, second, amount;

	public BoneMaskBlendNode( JsonObject node )
	{
		a = GraphFields.Reference( node, "A" );
		b = GraphFields.Reference( node, "B" );
		weight = GraphFields.Reference( node, "Weight" );
	}

	public override void Link( GraphNode[] nodes )
	{
		first = ValueNodes.Get( nodes, a );
		second = ValueNodes.Get( nodes, b );
		amount = ValueNodes.Get( nodes, weight );
	}

	protected override int OnMask( GraphContext context )
	{
		var at = Math.Clamp( amount?.Float( context ) ?? 0.0f, 0.0f, 1.0f );

		var left = first?.Mask( context ) ?? -1;
		var right = second?.Mask( context ) ?? -1;

		if ( left < 0 ) return right;
		if ( right < 0 ) return left;

		context.BlendMask( left, left, right, at );
		context.FreeMask( right );

		return left;
	}
}

//One mask or another, by a condition - and, when asked, blended between them rather than swapped.
internal sealed class BoneMaskSwitchNode : GraphNode
{
	private readonly int condition, whenTrue, whenFalse;
	private readonly bool dynamic;
	private readonly float blendTime;

	private GraphNode test, yes, no;

	private float blend;
	private int frame = -1;

	public BoneMaskSwitchNode( JsonObject node )
	{
		condition = GraphFields.Reference( node, "Condition" );
		whenTrue = GraphFields.Reference( node, "True" );
		whenFalse = GraphFields.Reference( node, "False" );

		dynamic = GraphFields.Flag( node, "SwitchDynamically" );
		blendTime = GraphFields.Number( node, "BlendTime" );
	}

	public override void Link( GraphNode[] nodes )
	{
		test = ValueNodes.Get( nodes, condition );
		yes = ValueNodes.Get( nodes, whenTrue );
		no = ValueNodes.Get( nodes, whenFalse );
	}

	protected override int OnMask( GraphContext context )
	{
		var wanted = ( test?.Bool( context ) ?? false ) ? 1.0f : 0.0f;

		if ( frame != context.Frame )
		{
			frame = context.Frame;

			if ( !dynamic || blendTime <= 0.0f )
			{
				blend = wanted;
			}
			else
			{
				var step = context.Delta / blendTime;
				blend = wanted > blend ? MathF.Min( blend + step, 1.0f ) : MathF.Max( blend - step, 0.0f );
			}
		}

		if ( blend <= 0.0f ) return no?.Mask( context ) ?? -1;
		if ( blend >= 1.0f ) return yes?.Mask( context ) ?? -1;

		var left = no?.Mask( context ) ?? -1;
		var right = yes?.Mask( context ) ?? -1;

		if ( left < 0 ) return right;
		if ( right < 0 ) return left;

		context.BlendMask( left, left, right, blend );
		context.FreeMask( right );

		return left;
	}
}

//The mask whose name matches.
internal sealed class BoneMaskSelectNode : GraphNode
{
	private readonly int id, fallback;
	private readonly string[] ids;
	private readonly int[] masks;

	private GraphNode input, spare;
	private GraphNode[] options = Array.Empty<GraphNode>();

	public BoneMaskSelectNode( JsonObject node )
	{
		id = GraphFields.Reference( node, "Id" );
		fallback = GraphFields.Reference( node, "Default" );

		var list = GraphFields.Array( node, "Options" );
		var count = list?.Count ?? 0;

		ids = new string[count];
		masks = new int[count];

		for ( var i = 0; i < count; i++ )
		{
			if ( list[i] is not JsonObject entry ) continue;

			ids[i] = GraphFields.Text( entry, "Id" );
			masks[i] = GraphFields.Reference( entry, "Mask" );
		}
	}

	public override void Link( GraphNode[] nodes )
	{
		input = ValueNodes.Get( nodes, id );
		spare = ValueNodes.Get( nodes, fallback );

		options = new GraphNode[masks.Length];
		for ( var i = 0; i < masks.Length; i++ ) options[i] = ValueNodes.Get( nodes, masks[i] );
	}

	protected override int OnMask( GraphContext context )
	{
		var wanted = input?.Id( context ) ?? "";

		for ( var i = 0; i < ids.Length; i++ )
		{
			if ( string.Equals( ids[i], wanted, StringComparison.OrdinalIgnoreCase ) ) return options[i]?.Mask( context ) ?? -1;
		}

		return spare?.Mask( context ) ?? -1;
	}
}
