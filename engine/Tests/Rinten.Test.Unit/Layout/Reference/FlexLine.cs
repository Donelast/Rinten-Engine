//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Rinten.Layout.Reference;

//What is known about one line while it is being laid out.
internal struct FlexLineRunningLayout
{
	//Grow factors still to be satisfied; counted down as room is handed out.
	public float TotalFlexGrowFactors;

	//Room along the main axis nobody has claimed yet.
	public float RemainingFreeSpace;

	//How far along the main axis the line reaches, once everything is placed.
	public float MainDim;

	//How thick the line is across.
	public float CrossDim;

}

//One run of items that fit on a single line. Rented from FlexLinePool, never allocated per pass.
internal sealed class FlexLine
{
	//The items that flow: not absolute, not display: none , and within what the line could take.
	public readonly List<LayoutNode> ItemsInFlow = new();

	//Where the items settle along the main axis, parallel to ItemsInFlow; the two lists beside it are the solver's scratch.
	public readonly List<float> TargetMainSize = new();
	public readonly List<bool> Frozen = new();
	public readonly List<float> Scratch = new();

	//What each item's shrink factor is scaled by, worked out once a solve rather than once a round.
	public readonly List<float> InnerBasis = new();

	//Their sizes and margins added up, which is what free space is measured against.
	public float SizeConsumed;

	//How many edges along the line have an automatic margin to soak up slack.
	public int NumberOfAutoMargins;

	public FlexLineRunningLayout Layout;

	public void Reset()
	{
		ItemsInFlow.Clear();
		TargetMainSize.Clear();
		Frozen.Clear();
		Scratch.Clear();
		InnerBasis.Clear();
		SizeConsumed = 0;
		NumberOfAutoMargins = 0;
		Layout = default;
	}
}

//Lines for the flex algorithm to fill. A layout of a container rents one line and reuses it for every line of that container; nested
//containers rent their own, so the pool is a stack. Per thread, because layouts on different threads must not hand each other their lines.
internal static class FlexLinePool
{
	private const int MaxPooled = 64;
	private const int MaxRetainedItems = 4096;

	[ThreadStatic] private static Stack<FlexLine> pool;

	public static FlexLine Rent()
	{
		pool ??= new Stack<FlexLine>();
		return pool.Count > 0 ? pool.Pop() : new FlexLine();
	}

	public static void Return( FlexLine line )
	{
		//A line that grew to hold a huge number of items is not worth keeping around.
		if ( line.ItemsInFlow.Capacity > MaxRetainedItems || pool.Count >= MaxPooled ) return;

		line.Reset();
		pool.Push( line );
	}
}

internal static class FlexLineBuilder
{
	//Fills a line from index until full (Yoga FlexLine.cpp); an oversized lone item stays and overflows
	public static void Calculate( FlexLine line, LayoutNode node, Direction ownerDirection, float ownerWidth,
		float childMainReference, float availableInnerWidth, float availableInnerMainDim,
		ref int index, int lineCount )
	{
		line.Reset();
		var itemsInFlow = line.ItemsInFlow;

		var sizeConsumed = 0.0f;
		var totalFlexGrowFactors = 0.0f;
		var numberOfAutoMargins = 0;
		LayoutNode firstElementInLine = null;

		var sizeConsumedIncludingMinConstraint = 0.0f;

		var direction = node.ResolveDirection( ownerDirection );
		var mainAxis = Axis.Resolve( node.Style.FlexDirection, direction );
		var isWrap = node.Style.FlexWrap != Wrap.NoWrap;
		var gap = node.Style.ComputeGapForAxis( mainAxis, availableInnerMainDim );

		var children = node.LayoutChildren;

		for ( ; index < children.Count; ++index )
		{
			var child = children[index];

			if ( child.Style.Display == Display.None || child.IsOutOfFlow )
				continue;

			firstElementInLine ??= child;

			if ( child.Style.FlexStartMarginIsAuto( mainAxis, ownerDirection ) ) numberOfAutoMargins++;
			if ( child.Style.FlexEndMarginIsAuto( mainAxis, ownerDirection ) ) numberOfAutoMargins++;

			child.LineIndex = lineCount;

			var childMarginMainAxis = child.Style.ComputeMarginForAxis( mainAxis, availableInnerWidth );
			var childLeadingGapMainAxis = ReferenceEquals( child, firstElementInLine ) ? 0.0f : gap;

			var flexBasisWithMinAndMaxConstraints = Bounds.WithinMinAndMax( child, direction, mainAxis,
				child.Layout.ComputedFlexBasis, childMainReference, availableInnerWidth );

			//The item that would push the line past what it has is the start of the next one - unless nothing is on this line yet.
			if ( sizeConsumedIncludingMinConstraint + flexBasisWithMinAndMaxConstraints
					+ childMarginMainAxis + childLeadingGapMainAxis > availableInnerMainDim
				&& isWrap && itemsInFlow.Count != 0 )
			{
				break;
			}

			sizeConsumedIncludingMinConstraint += flexBasisWithMinAndMaxConstraints + childMarginMainAxis + childLeadingGapMainAxis;
			sizeConsumed += flexBasisWithMinAndMaxConstraints + childMarginMainAxis + childLeadingGapMainAxis;

			if ( child.IsNodeFlexible() )
			{
				totalFlexGrowFactors += child.ResolveFlexGrow();
			}

			itemsInFlow.Add( child );
		}

		//A total under one is treated as one, or a single item with a factor of a half would take only half the room going spare.
		if ( totalFlexGrowFactors > 0 && totalFlexGrowFactors < 1 ) totalFlexGrowFactors = 1;

		line.SizeConsumed = sizeConsumed;
		line.NumberOfAutoMargins = numberOfAutoMargins;
		line.Layout.TotalFlexGrowFactors = totalFlexGrowFactors;
	}
}

internal static class Baseline
{
	//Baseline offset from the top (Yoga Baseline.cpp): first baseline-aligned or first child's bottom
	public static float Calculate( LayoutNode node )
	{
		//A control that knows its own baseline is asked, at the size it was measured at.
		if ( node.HasBaselineFunc )
			return node.CallBaselineFunc( node.Layout.MeasuredDimension( Dimension.Width ), node.Layout.MeasuredDimension( Dimension.Height ) );

		//A leaf's baseline is its bottom, read off its current measurement. A box's is part of its layout, computed when it was laid out: reading
		//its children now would read whatever the last measurement of each left behind, and the answer would depend on what was asked before.
		if ( node.HasMeasureFunc || node.ChildCount == 0 ) return node.Layout.MeasuredDimension( Dimension.Height );

		return node.Layout.Baseline;
	}

	//Whether a box contributes a baseline to a block that holds it: text does, a control with a baseline callback does, a flex container
	//with items does (one is synthesised from its first item), a block does where a child of its does, and an empty box does not.
	private static bool HasBaseline( LayoutNode box )
	{
		if ( box.HasBaselineFunc || box.HasMeasureFunc ) return true;

		foreach ( var child in box.LayoutChildren )
		{
			if ( child.Style.Display == Display.None || child.IsOutOfFlow ) continue;
			if ( box.Style.Display != Display.Block || HasBaseline( child ) ) return true;
		}

		return false;
	}

	//The walk itself, run at the end of a box's layout while its children are where it put them.
	public static float Compute( LayoutNode node )
	{
		//A block's baseline is its first in-flow child's that has one (CSS Align 9.1); a child with none - an empty box - is
		//passed over, and a block with none at all has none, which the owner then synthesises from the block's bottom edge.
		if ( node.Style.Display == Display.Block )
		{
			foreach ( var child in node.LayoutChildren )
			{
				if ( child.Style.Display == Display.None || child.IsOutOfFlow || !HasBaseline( child ) ) continue;
				return Calculate( child ) + child.Layout.Position( PhysicalEdge.Top );
			}

			return node.Layout.MeasuredDimension( Dimension.Height );
		}

		LayoutNode baselineChild = null;

		foreach ( var child in node.LayoutChildren )
		{
			if ( child.LineIndex > 0 ) break;
			//A hidden child has no baseline; it was zeroed, never measured.
			if ( child.Style.Display == Display.None ) continue;
			if ( child.IsOutOfFlow ) continue;

			if ( Axis.ResolveChildAlignment( node, child ) == Align.Baseline || child.IsReferenceBaseline )
			{
				baselineChild = child;
				break;
			}

			baselineChild ??= child;
		}

		if ( baselineChild is null ) return node.Layout.MeasuredDimension( Dimension.Height );

		return Calculate( baselineChild ) + baselineChild.Layout.Position( PhysicalEdge.Top );
	}

	//Whether anything in this node lines up on a baseline.
	public static bool IsBaselineLayout( LayoutNode node )
	{
		if ( Axis.IsColumn( node.Style.FlexDirection ) ) return false;
		if ( node.Style.AlignItems == Align.Baseline ) return true;

		foreach ( var child in node.LayoutChildren )
		{
			if ( !child.IsOutOfFlow && child.Style.Display != Display.None && child.Style.AlignSelf == Align.Baseline )
				return true;
		}

		return false;
	}
}
