//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Rinten.Layout.Reference;

//What a node's measure function is asked, and what it answers.
internal delegate LayoutSize MeasureFunc( LayoutNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode );

//Where a node's baseline is, asked at the size the node was measured at; the answer is a distance down from its top.
internal delegate float BaselineFunc( LayoutNode node, float width, float height );

//A width and a height, as a measure function hands them back.
internal struct LayoutSize
{
	public float Width;
	public float Height;

	public LayoutSize( float width, float height )
	{
		Width = width;
		Height = height;
	}
}

//A layout box (Yoga Node) without copy-on-write or display: contents; results stored inline
internal sealed class LayoutNode
{
	//Leaves share one empty list so a walk over "the children" never allocates and never boxes
	//an enumerator; nothing may add to it, and InsertChild makes a node its own list first.
	private static readonly List<LayoutNode> NoChildren = new( 0 );

	public LayoutStyle Style { get; }

	private LayoutResults layout = LayoutResults.Create();

	//Where the last layout put the node, by reference: hold it as ref var , never copy it.
	public ref LayoutResults Layout => ref layout;

	private LayoutConfig config;
	private List<LayoutNode> children;
	private SizeLength processedWidth = SizeLength.Undefined;
	private SizeLength processedHeight = SizeLength.Undefined;

	private MeasureFunc measure;
	private BaselineFunc baseline;

	//The margins a block's last answer let out through its edges, per cache slot; null for anything that has never been laid out as a block.
	internal BlockMargins BlockMargins;

	public LayoutNode Owner { get; private set; }

	public bool HasNewLayout { get; set; } = true;
	public bool IsDirty { get; private set; } = true;
	public bool IsReferenceBaseline { get; set; }
	public NodeType NodeType { get; set; } = NodeType.Default;
	public int LineIndex { get; set; }
	public bool AlwaysFormsContainingBlock { get; set; }

	//Whatever the caller wants to hang off the node; the interface keeps its panel here.
	public object Context { get; set; }

	public LayoutNode() : this( LayoutConfig.Default )
	{
	}

	public LayoutNode( LayoutConfig config )
	{
		Style = new LayoutStyle( this );
		this.config = config ?? LayoutConfig.Default;

		if ( this.config.UseWebDefaults ) UseWebDefaults();
	}

	//What a browser would have defaulted to, rather than what Yoga does: a row rather than a column, and content stretched rather than packed.
	private void UseWebDefaults()
	{
		Style.FlexDirection = FlexDirection.Row;
		Style.AlignContent = Align.Stretch;
	}

	public LayoutConfig Config => config;

	public void SetConfig( LayoutConfig value )
	{
		if ( value is null || ReferenceEquals( value, config ) ) return;

		if ( LayoutConfig.UpdateInvalidatesLayout( config, value ) )
		{
			MarkDirtyAndPropagate();
			layout.ConfigVersion = 0;
		}
		else
		{
			//The same flags by another name: the cache is still good, so the stamp is moved across rather than thrown away.
			layout.ConfigVersion = value.Version;
		}

		config = value;
	}

	public bool HasErrata( int errata ) => config.HasErrata( errata );

	//-- the tree -------------------------------------------------------------------

	//The children, read only: the tree is changed through InsertChild and RemoveChild, which keep the owners and the subtree counts
	//right. A view over the live list, made once. The walks inside the engine take RawChildren, whose foreach does not allocate.
	public ReadOnlyCollection<LayoutNode> Children => children is null ? NoChildrenView : childrenView ??= new ReadOnlyCollection<LayoutNode>( children );

	private ReadOnlyCollection<LayoutNode> childrenView;
	private static readonly ReadOnlyCollection<LayoutNode> NoChildrenView = new( NoChildren );

	//The live list itself, or the shared empty one for a leaf. Nothing may add to it.
	internal List<LayoutNode> RawChildren => children ?? NoChildren;

	//The children layout walks: the children themselves, except that a child with display: contents is replaced by its own layout
	//children, all the way down. The same list as Children where no child is such, and a flattened copy kept up to date otherwise.
	public List<LayoutNode> LayoutChildren => contentsChildren == 0 ? children ?? NoChildren : Flattened();

	//How many direct children have display: contents, and the flattened list those call for.
	private int contentsChildren;
	private List<LayoutNode> flattened;
	private bool flattenedDirty = true;

	private List<LayoutNode> Flattened()
	{
		if ( !flattenedDirty ) return flattened;

		flattened ??= new List<LayoutNode>( children.Count );
		flattened.Clear();

		foreach ( var child in children )
		{
			if ( child.Style.Display == Display.Contents ) flattened.AddRange( child.LayoutChildren );
			else flattened.Add( child );
		}

		flattenedDirty = false;
		return flattened;
	}

	//The flattened list is stale: a child came or went, or changed its display, here or under a contents child.
	private void InvalidateLayoutChildren()
	{
		flattenedDirty = true;

		//A contents node's children are its owner's layout children too.
		if ( Style.Display == Display.Contents ) Owner?.InvalidateLayoutChildren();
	}

	public int ChildCount => children?.Count ?? 0;

	public LayoutNode ChildAt( int index ) => children[index];

	public void InsertChild( LayoutNode child, int index )
	{
		if ( child is null ) return;

		//A node under itself, or under one of its own descendants, would close a ring that every
		//walk below would run round for ever. A node already in a tree is moved, not shared.
		for ( var n = this; n is not null; n = n.Owner )
		{
			if ( ReferenceEquals( n, child ) )
				throw new InvalidOperationException( "a node cannot be inserted under itself or under one of its descendants" );
		}

		child.Owner?.RemoveChild( child );

		children ??= new List<LayoutNode>( 4 );
		index = Math.Clamp( index, 0, children.Count );

		children.Insert( index, child );
		child.Owner = this;
		AddSubtreeCounters( child, 1 );
		if ( child.Style.IsContents ) contentsChildren++;
		InvalidateLayoutChildren();

		MarkDirtyAndPropagate();
	}

	public bool RemoveChild( LayoutNode child )
	{
		if ( child is null || children is null || !children.Remove( child ) ) return false;

		AddSubtreeCounters( child, -1 );
		if ( child.Style.IsContents ) contentsChildren--;
		InvalidateLayoutChildren();
		child.Owner = null;
		child.layout.Reset();

		MarkDirtyAndPropagate();
		return true;
	}

	public void RemoveAllChildren()
	{
		if ( children is null || children.Count == 0 ) return;

		foreach ( var child in children )
		{
			AddSubtreeCounters( child, -1 );
			if ( child.Style.IsContents ) contentsChildren--;
			child.Owner = null;
			child.layout.Reset();
		}

		children.Clear();
		InvalidateLayoutChildren();
		MarkDirtyAndPropagate();
	}

	//-- what the subtree holds -------------------------------------------------------

	//Counted over this node and everything below it, kept current on every style change
	//and every insert or remove, so the cache can ask in constant time instead of walking.
	private int subtreePercentUsers;
	private int subtreeReuseBlockers;
	private int subtreeBaselineUsers;
	private int subtreeFixedNodes;

	//Whether a percentage anywhere in the subtree resolves against an owner's size.
	public bool SubtreeUsesPercent => subtreePercentUsers > 0;

	//Whether anything in the subtree can make an answer depend on the room offered rather than on the content. See LayoutStyle.BlocksReuse.
	public bool SubtreeBlocksReuse => subtreeReuseBlockers > 0;

	//Whether anything in the subtree lines up on a baseline, this node's own items included.
	public bool SubtreeUsesBaseline => subtreeBaselineUsers > 0;

	//Whether anything in the subtree, this node included, is placed against the root; the root walks only where it is.
	public bool SubtreeHasFixed => subtreeFixedNodes > 0;

	//Whether the node takes no part in its owner's flow: absolute, or fixed against the root.
	public bool IsOutOfFlow => Style.PositionType is PositionType.Absolute or PositionType.Fixed;

	//Whether any direct child is only its contents, so the layout children are the flattened list.
	public bool HasContentsChildren => contentsChildren > 0;

	internal void StyleTraitsChanged( bool wasPercent, bool didBlock, bool wasBaseline, bool wasContents, bool wasFixed )
	{
		AddToCounters( ( Style.UsesPercent ? 1 : 0 ) - ( wasPercent ? 1 : 0 ),
			( Style.BlocksReuse ? 1 : 0 ) - ( didBlock ? 1 : 0 ),
			( Style.UsesBaseline ? 1 : 0 ) - ( wasBaseline ? 1 : 0 ),
			( Style.IsFixed ? 1 : 0 ) - ( wasFixed ? 1 : 0 ) );

		if ( Style.IsContents != wasContents && Owner is not null )
		{
			Owner.contentsChildren += Style.IsContents ? 1 : -1;
			Owner.InvalidateLayoutChildren();
		}
	}

	private void AddSubtreeCounters( LayoutNode child, int sign )
		=> AddToCounters( sign * child.subtreePercentUsers, sign * child.subtreeReuseBlockers, sign * child.subtreeBaselineUsers, sign * child.subtreeFixedNodes );

	private void AddToCounters( int percentDelta, int blockDelta, int baselineDelta, int fixedDelta )
	{
		if ( percentDelta == 0 && blockDelta == 0 && baselineDelta == 0 && fixedDelta == 0 ) return;

		for ( var n = this; n is not null; n = n.Owner )
		{
			n.subtreePercentUsers += percentDelta;
			n.subtreeReuseBlockers += blockDelta;
			n.subtreeBaselineUsers += baselineDelta;
			n.subtreeFixedNodes += fixedDelta;
		}
	}

	//-- measuring ------------------------------------------------------------------

	public bool HasMeasureFunc => measure is not null;

	public void SetMeasureFunc( MeasureFunc value )
	{
		//A node that measures itself has nothing below it to measure.
		if ( value is null )
		{
			NodeType = NodeType.Default;
		}
		else
		{
			if ( ChildCount != 0 )
				throw new InvalidOperationException( "a node with children cannot measure itself" );

			NodeType = NodeType.Text;
		}

		measure = value;
		MarkDirtyAndPropagate();
	}

	//Ask the node how big it wants to be. A measure that answers with nonsense - a negative, or
	//nothing at all - is clamped rather than trusted, which is what Yoga does after warning about it.
	public LayoutSize Measure( float availableWidth, MeasureMode widthMode, float availableHeight, MeasureMode heightMode )
	{
		var size = measure( this, availableWidth, widthMode, availableHeight, heightMode );

		if ( Num.IsUndefined( size.Height ) || size.Height < 0 ||
			Num.IsUndefined( size.Width ) || size.Width < 0 )
		{
			return new LayoutSize( Num.MaxOrDefined( 0.0f, size.Width ), Num.MaxOrDefined( 0.0f, size.Height ) );
		}

		return size;
	}

	//Whether a control has said where its baseline is - a text leaf's first line, say.
	public bool HasBaselineFunc => baseline is not null;

	//Installs the baseline callback, or removes it with null. A box with children may carry one too; it then speaks for its contents.
	public void SetBaselineFunc( BaselineFunc value )
	{
		if ( ReferenceEquals( baseline, value ) ) return;
		baseline = value;
		MarkDirtyAndPropagate();
	}

	public float CallBaselineFunc( float width, float height ) => baseline( this, width, height );

	//Not reachable through the interface: no min-content measure is ever installed. It
	//answers as an absent one does, so the algorithm's checks read the same as the C++.

	public bool HasMinContentMeasureFunc => false;
	public float MinContentWidth => Num.Undefined;
	public float MinContentHeight => Num.Undefined;

	//-- dirt -----------------------------------------------------------------------

	public void SetDirty( bool value )
	{
		if ( value == IsDirty ) return;

		IsDirty = value;
	}

	public void MarkDirtyAndPropagate()
	{
		if ( IsDirty ) return;

		SetDirty( true );
		layout.ComputedFlexBasis = Num.Undefined;

		Owner?.MarkDirtyAndPropagate();
	}

	//What the public markDirty does: only a node that measures itself may be dirtied by hand.
	public void MarkDirty()
	{
		if ( !HasMeasureFunc ) return;

		MarkDirtyAndPropagate();
	}

	//Back to a fresh node, keeping its config. Refused while it is still attached.
	public void Reset()
	{
		if ( ChildCount != 0 )
			throw new InvalidOperationException( "a node with children cannot be reset" );

		if ( Owner is not null )
			throw new InvalidOperationException( "a node with an owner cannot be reset" );

		Style.CopyFrom( new LayoutStyle() );
		layout.Reset();

		measure = null;
		baseline = null;
		Context = null;
		NodeType = NodeType.Default;
		LineIndex = 0;
		IsReferenceBaseline = false;
		AlwaysFormsContainingBlock = false;
		HasNewLayout = true;
		IsDirty = true;

		processedWidth = SizeLength.Undefined;
		processedHeight = SizeLength.Undefined;

		if ( config.UseWebDefaults ) UseWebDefaults();
	}

	//-- sizes --------------------------------------------------------------------

	public SizeLength ProcessedDimension( Dimension dimension ) => dimension == Dimension.Width ? processedWidth : processedHeight;

	//A minimum and a maximum that agree pin the size, whatever the size said. Worked out once a layout, before anything reads a dimension.
	public void ProcessDimensions()
	{
		processedWidth = ProcessDimension( Dimension.Width );
		processedHeight = ProcessDimension( Dimension.Height );
	}

	private SizeLength ProcessDimension( Dimension dimension )
	{
		if ( !Style.HasMinOrMaxDimension( dimension ) ) return Style.GetDimension( dimension );

		var max = Style.MaxDimension( dimension );
		var min = Style.MinDimension( dimension );

		return max.IsDefined && max.Equals( min ) ? max : Style.GetDimension( dimension );
	}

	//Whether the node has a definite length along a dimension.
	public bool HasDefiniteLength( Dimension dimension, float ownerSize )
	{
		var used = ProcessedDimension( dimension ).Resolve( ownerSize );

		return Num.IsDefined( used ) && used >= 0.0f;
	}

	public float GetResolvedDimension( Direction direction, Dimension dimension, float reference, float ownerWidth )
	{
		var value = ProcessedDimension( dimension ).Resolve( reference );

		if ( Style.BoxSizing == BoxSizing.BorderBox ) return value;

		var extra = Style.ComputePaddingAndBorderForDimension( direction, dimension, ownerWidth );

		return value + (Num.IsDefined( extra ) ? extra : 0.0f);
	}

	//The flex basis an author wrote, or what flex implies where they wrote none.
	public SizeLength ProcessFlexBasis()
	{
		var basis = Style.FlexBasis;
		if ( !basis.IsAuto && !basis.IsUndefined ) return basis;

		if ( Num.IsDefined( Style.Flex ) && Style.Flex > 0.0f )
			return config.UseWebDefaults ? SizeLength.Auto : SizeLength.Points( 0 );

		return SizeLength.Auto;
	}

	public float ResolveFlexBasis( Direction direction, FlexDirection flexDirection, float reference, float ownerWidth )
	{
		var value = ProcessFlexBasis().Resolve( reference );

		if ( Style.BoxSizing == BoxSizing.BorderBox ) return value;

		var extra = Style.ComputePaddingAndBorderForDimension( direction, Axis.DimensionOf( flexDirection ), ownerWidth );

		return value + (Num.IsDefined( extra ) ? extra : 0.0f);
	}

	//A root never grows: there is nothing above it to take room from.
	public float ResolveFlexGrow()
	{
		if ( Owner is null ) return 0.0f;

		if ( Num.IsDefined( Style.FlexGrow ) ) return Style.FlexGrow;
		if ( Num.IsDefined( Style.Flex ) && Style.Flex > 0.0f ) return Style.Flex;

		return LayoutStyle.DefaultFlexGrow;
	}

	public float ResolveFlexShrink()
	{
		if ( Owner is null ) return 0.0f;

		if ( Num.IsDefined( Style.FlexShrink ) ) return Style.FlexShrink;

		if ( !config.UseWebDefaults && Num.IsDefined( Style.Flex ) && Style.Flex < 0.0f )
			return -Style.Flex;

		return config.UseWebDefaults ? LayoutStyle.WebDefaultFlexShrink : LayoutStyle.DefaultFlexShrink;
	}

	public bool IsNodeFlexible()
		=> !IsOutOfFlow && (ResolveFlexGrow() != 0 || ResolveFlexShrink() != 0);

	public float DimensionWithMargin( FlexDirection axis, float widthSize )
		=> layout.MeasuredDimension( Axis.DimensionOf( axis ) ) + Style.ComputeMarginForAxis( axis, widthSize );

	public bool IsLayoutDimensionDefined( FlexDirection axis )
	{
		var value = layout.MeasuredDimension( Axis.DimensionOf( axis ) );

		return Num.IsDefined( value ) && value >= 0.0f;
	}

	//The direction the node lays out in, given what it inherits.
	public Direction ResolveDirection( Direction ownerDirection )
	{
		if ( Style.Direction == Direction.Inherit )
			return ownerDirection != Direction.Inherit ? ownerDirection : Direction.LTR;

		return Style.Direction;
	}

	//-- positions ------------------------------------------------------------------

	//Where an inset moves the node from where it would otherwise sit. The start wins where both ends are given; a static node has no insets at all.
	public float RelativePosition( FlexDirection axis, Direction direction, float axisSize )
	{
		if ( Style.PositionType == PositionType.Static ) return 0;

		if ( Style.IsInlineStartPositionDefined( axis, direction ) && !Style.IsInlineStartPositionAuto( axis, direction ) )
			return Style.ComputeInlineStartPosition( axis, direction, axisSize );

		return -1 * Style.ComputeInlineEndPosition( axis, direction, axisSize );
	}

	public void SetPosition( Direction direction, float ownerWidth, float ownerHeight )
	{
		//A root is always laid out left to right, so nothing comes back negative.
		var respectingRoot = Owner is not null ? direction : Direction.LTR;

		var mainAxis = Axis.Resolve( Style.FlexDirection, respectingRoot );
		var crossAxis = Axis.ResolveCross( mainAxis, respectingRoot );

		var relativeMain = RelativePosition( mainAxis, respectingRoot, Axis.IsRow( mainAxis ) ? ownerWidth : ownerHeight );
		var relativeCross = RelativePosition( crossAxis, respectingRoot, Axis.IsRow( mainAxis ) ? ownerHeight : ownerWidth );

		layout.SetPosition( Axis.InlineStartEdge( mainAxis, direction ),
			Style.ComputeInlineStartMargin( mainAxis, direction, ownerWidth ) + relativeMain );

		layout.SetPosition( Axis.InlineEndEdge( mainAxis, direction ),
			Style.ComputeInlineEndMargin( mainAxis, direction, ownerWidth ) + relativeMain );

		layout.SetPosition( Axis.InlineStartEdge( crossAxis, direction ),
			Style.ComputeInlineStartMargin( crossAxis, direction, ownerWidth ) + relativeCross );

		layout.SetPosition( Axis.InlineEndEdge( crossAxis, direction ),
			Style.ComputeInlineEndMargin( crossAxis, direction, ownerWidth ) + relativeCross );
	}

	//-- what the layout wrote --------------------------------------------------------

	public void SetLayoutDimension( float value, Dimension dimension )
	{
		layout.SetDimension( dimension, value );
		layout.SetRawDimension( dimension, value );
	}

	public void SetLayoutMeasuredDimension( float value, Dimension dimension ) => layout.SetMeasuredDimension( dimension, value );

	public void SetLayoutComputedFlexBasis( float value ) => layout.ComputedFlexBasis = value;

	public override string ToString() => $"LayoutNode [{layout.Position( PhysicalEdge.Left )}, {layout.Position( PhysicalEdge.Top )}, {layout.GetDimension( Dimension.Width )} x {layout.GetDimension( Dimension.Height )}] children={ChildCount}";
}
