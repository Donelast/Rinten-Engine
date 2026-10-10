//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace Rinten.Layout.Reference;

//The nine edges an author can name, stored inline in the style.
[InlineArray( LayoutStyle.EdgeCount )]
internal struct EdgeValues
{
	private StyleLength element0;
}

[InlineArray( LayoutStyle.GutterCount )]
internal struct GutterValues
{
	private StyleLength element0;
}

[InlineArray( LayoutStyle.DimensionCount )]
internal struct SizeValues
{
	private SizeLength element0;
}

//Box style (Yoga Style.h) with masked inline edge tables; setters dirty the node only on change
internal sealed class LayoutStyle
{
	public const float DefaultFlexGrow = 0.0f;
	public const float DefaultFlexShrink = 0.0f;
	public const float WebDefaultFlexShrink = 1.0f;

	public const int EdgeCount = 9;
	public const int GutterCount = 3;
	public const int DimensionCount = 2;

	private readonly LayoutNode node;

	//A style with no node behind it: a template to copy from, or a value in a test.
	public LayoutStyle() : this( null )
	{
	}

	public LayoutStyle( LayoutNode node )
	{
		this.node = node;
		dimensions[0] = SizeLength.Auto;
		dimensions[1] = SizeLength.Auto;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private void Dirty() => node?.MarkDirtyAndPropagate();

	//For the setters that can move a trait: dirty, then look again.
	private void Changed()
	{
		Dirty();
		Retrait();
	}

	//-- traits ----------------------------------------------------------------------

	private bool usesPercent;
	private bool usesPercentBesidesSize;
	private bool blocksReuse;

	//Whether any length in this style is a percentage, and so resolves against an owner's size.
	public bool UsesPercent => usesPercent;

	//The same, not counting the width and height: an owner resolves those before it asks, and hands the result over as the room, so a leaf's
	//answer does not depend on the owner through them. A box's can, where it resolves its own cross size against the owner as it lays out.
	public bool UsesPercentBesidesSize => usesPercentBesidesSize;

	//Whether a measurement of this node taken under one constraint may be handed out under another that its answer fits. A percentage, wrapping, an
	//aspect ratio or a scrolling box can each make the answer depend on the room offered rather than on the content alone; nothing else in a style can.
	public bool BlocksReuse => blocksReuse;

	private bool usesBaseline;

	//Whether this node lines its items up on a baseline, or asks to be lined up on one.
	public bool UsesBaseline => usesBaseline;

	private bool isContents;

	//Whether this node is only its contents: no box of its own, its children laid out as its owner's.
	public bool IsContents => isContents;

	private bool isFixed;

	//Whether this node is placed against the root of the tree rather than by its owner.
	public bool IsFixed => isFixed;

	private void Retrait()
	{
		var percentBesidesSize = AnyPercent();
		var percent = percentBesidesSize || dimensions[0].IsPercent || dimensions[1].IsPercent;
		var baseline = alignItems == Align.Baseline || alignSelf == Align.Baseline;
		var contents = display == Display.Contents;
		var fixedPosition = positionType == PositionType.Fixed;
		usesPercentBesidesSize = percentBesidesSize;

		//Also blocks reuse: max sizes, definite flex basis, baseline alignment and grid containers
		var blocks = percent || baseline
			|| display == Display.Grid
			|| flexWrap != Wrap.NoWrap || overflow == Overflow.Scroll || Num.IsDefined( aspectRatio )
			|| ( minMaxMask & ( 0b11u << DimensionCount ) ) != 0
			|| flexBasis.IsPoints || flexBasis.IsPercent;

		if ( percent == usesPercent && blocks == blocksReuse && baseline == usesBaseline && contents == isContents && fixedPosition == isFixed ) return;

		var wasPercent = usesPercent;
		var didBlock = blocksReuse;
		var wasBaseline = usesBaseline;
		var wasContents = isContents;
		var wasFixed = isFixed;
		usesPercent = percent;
		blocksReuse = blocks;
		usesBaseline = baseline;
		isContents = contents;
		isFixed = fixedPosition;
		node?.StyleTraitsChanged( wasPercent, didBlock, wasBaseline, wasContents, wasFixed );
	}

	private bool AnyPercent()
	{
		for ( var i = 0; i < EdgeCount; ++i )
		{
			if ( margin[i].IsPercent || position[i].IsPercent || padding[i].IsPercent || border[i].IsPercent ) return true;
		}

		for ( var i = 0; i < GutterCount; ++i )
		{
			if ( gap[i].IsPercent ) return true;
		}

		for ( var i = 0; i < DimensionCount; ++i )
		{
			if ( minDimensions[i].IsPercent || maxDimensions[i].IsPercent ) return true;
		}

		return flexBasis.IsPercent;
	}

	//-- the enumerations ------------------------------------------------------------

	private Direction direction = Direction.Inherit;
	public Direction Direction
	{
		get => direction;
		set { if ( direction == value ) return; direction = value; Dirty(); }
	}

	private FlexDirection flexDirection = FlexDirection.Column;
	public FlexDirection FlexDirection
	{
		get => flexDirection;
		set { if ( flexDirection == value ) return; flexDirection = value; Dirty(); }
	}

	private Justify justifyContent = Justify.FlexStart;
	public Justify JustifyContent
	{
		get => justifyContent;
		set { if ( justifyContent == value ) return; justifyContent = value; Dirty(); }
	}

	private Align alignContent = Align.FlexStart;
	public Align AlignContent
	{
		get => alignContent;
		set { if ( alignContent == value ) return; alignContent = value; Dirty(); }
	}

	private Align alignItems = Align.Stretch;
	public Align AlignItems
	{
		get => alignItems;
		set { if ( alignItems == value ) return; alignItems = value; Changed(); }
	}

	private Align alignSelf = Align.Auto;
	public Align AlignSelf
	{
		get => alignSelf;
		set { if ( alignSelf == value ) return; alignSelf = value; Changed(); }
	}

	private PositionType positionType = PositionType.Relative;
	public PositionType PositionType
	{
		get => positionType;
		set { if ( positionType == value ) return; positionType = value; Changed(); }
	}

	private Wrap flexWrap = Wrap.NoWrap;
	public Wrap FlexWrap
	{
		get => flexWrap;
		set { if ( flexWrap == value ) return; flexWrap = value; Changed(); }
	}

	private Overflow overflow = Overflow.Visible;
	public Overflow Overflow
	{
		get => overflow;
		set { if ( overflow == value ) return; overflow = value; Changed(); }
	}

	private Display display = Display.Flex;
	public Display Display
	{
		get => display;
		set { if ( display == value ) return; display = value; Changed(); }
	}

	private BoxSizing boxSizing = BoxSizing.BorderBox;
	public BoxSizing BoxSizing
	{
		get => boxSizing;
		set { if ( boxSizing == value ) return; boxSizing = value; Dirty(); }
	}

	//-- grid ---------------------------------------------------------------------------

	//The grid properties live in a record made on the first non-default value, so a node that
	//is never a grid or a grid item pays one reference for them rather than seventy bytes.
	private GridStyleValues grid;

	private GridStyleValues Grid() => grid ??= new GridStyleValues();

	public GridTrackList GridTemplateColumns
	{
		get => grid?.TemplateColumns ?? GridTrackList.None;
		set { value ??= GridTrackList.None; if ( GridTemplateColumns.Equals( value ) ) return; Grid().TemplateColumns = value; Dirty(); }
	}

	public GridTrackList GridTemplateRows
	{
		get => grid?.TemplateRows ?? GridTrackList.None;
		set { value ??= GridTrackList.None; if ( GridTemplateRows.Equals( value ) ) return; Grid().TemplateRows = value; Dirty(); }
	}

	public GridTrackList GridAutoColumns
	{
		get => grid?.AutoColumns ?? GridTrackList.SingleAuto;
		set { value = value is null || value.IsEmpty ? GridTrackList.SingleAuto : value; if ( GridAutoColumns.Equals( value ) ) return; Grid().AutoColumns = value; Dirty(); }
	}

	public GridTrackList GridAutoRows
	{
		get => grid?.AutoRows ?? GridTrackList.SingleAuto;
		set { value = value is null || value.IsEmpty ? GridTrackList.SingleAuto : value; if ( GridAutoRows.Equals( value ) ) return; Grid().AutoRows = value; Dirty(); }
	}

	public GridAutoFlow GridAutoFlow
	{
		get => grid?.AutoFlow ?? GridAutoFlow.Row;
		set { if ( GridAutoFlow == value ) return; Grid().AutoFlow = value; Dirty(); }
	}

	public GridLine GridColumnStart { get => grid?.ColumnStart ?? GridLine.Auto; set { if ( GridColumnStart == value ) return; Grid().ColumnStart = value; Dirty(); } }
	public GridLine GridColumnEnd { get => grid?.ColumnEnd ?? GridLine.Auto; set { if ( GridColumnEnd == value ) return; Grid().ColumnEnd = value; Dirty(); } }
	public GridLine GridRowStart { get => grid?.RowStart ?? GridLine.Auto; set { if ( GridRowStart == value ) return; Grid().RowStart = value; Dirty(); } }
	public GridLine GridRowEnd { get => grid?.RowEnd ?? GridLine.Auto; set { if ( GridRowEnd == value ) return; Grid().RowEnd = value; Dirty(); } }

	//How a grid places its items along the row axis, and how an item asks to be placed; the block axis uses the align properties.
	public Align JustifyItems { get => grid?.JustifyItems ?? Align.Stretch; set { if ( JustifyItems == value ) return; Grid().JustifyItems = value; Dirty(); } }
	public Align JustifySelf { get => grid?.JustifySelf ?? Align.Auto; set { if ( JustifySelf == value ) return; Grid().JustifySelf = value; Dirty(); } }

	//-- the numbers -----------------------------------------------------------------

	private float flex = Num.Undefined;
	public float Flex
	{
		get => flex;
		set { if ( Num.EqualsOptional( flex, value ) ) return; flex = value; Dirty(); }
	}

	private float flexGrow = Num.Undefined;
	public float FlexGrow
	{
		get => flexGrow;
		set { if ( Num.EqualsOptional( flexGrow, value ) ) return; flexGrow = value; Dirty(); }
	}

	private float flexShrink = Num.Undefined;
	public float FlexShrink
	{
		get => flexShrink;
		set { if ( Num.EqualsOptional( flexShrink, value ) ) return; flexShrink = value; Dirty(); }
	}

	private SizeLength flexBasis = SizeLength.Auto;
	public SizeLength FlexBasis
	{
		get => flexBasis;
		set { if ( flexBasis == value ) return; flexBasis = value; Changed(); }
	}

	private float aspectRatio = Num.Undefined;
	public float AspectRatio
	{
		get => aspectRatio;
		set
		{
			//A ratio of nought or infinity is not a ratio; CSS says treat it as auto.
			var ratio = value == 0.0f || float.IsInfinity( value ) ? Num.Undefined : value;
			if ( Num.EqualsOptional( aspectRatio, ratio ) ) return;
			aspectRatio = ratio;
			Changed();
		}
	}

	//-- the tables ------------------------------------------------------------------

	private EdgeValues margin;
	private EdgeValues position;
	private EdgeValues padding;
	private EdgeValues border;
	private GutterValues gap;

	private SizeValues dimensions;
	private SizeValues minDimensions;
	private SizeValues maxDimensions;

	//Bit i is set when the entry for Edge i is defined. The resolution chains below test the mask instead of loading entries, and
	//an empty mask lets the hot sums return 0 at once. For min and max, bit (axis) is the minimum and bit (2 + axis) the maximum.
	private uint marginMask;
	private uint positionMask;
	private uint paddingMask;
	private uint borderMask;
	private uint minMaxMask;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static uint WithBit( uint mask, int bit, bool set ) => set ? mask | (1u << bit) : mask & ~(1u << bit);

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool Has( uint mask, Edge edge ) => (mask & (1u << (int)edge)) != 0;

	public StyleLength Margin( Edge edge ) => margin[(int)edge];

	public void SetMargin( Edge edge, StyleLength value )
	{
		if ( margin[(int)edge] == value ) return;
		margin[(int)edge] = value;
		marginMask = WithBit( marginMask, (int)edge, value.IsDefined );
		Changed();
	}

	public StyleLength Position( Edge edge ) => position[(int)edge];

	public void SetPosition( Edge edge, StyleLength value )
	{
		if ( position[(int)edge] == value ) return;
		position[(int)edge] = value;
		positionMask = WithBit( positionMask, (int)edge, value.IsDefined );
		Changed();
	}

	public StyleLength Padding( Edge edge ) => padding[(int)edge];

	public void SetPadding( Edge edge, StyleLength value )
	{
		if ( padding[(int)edge] == value ) return;
		padding[(int)edge] = value;
		paddingMask = WithBit( paddingMask, (int)edge, value.IsDefined );
		Changed();
	}

	public StyleLength Border( Edge edge ) => border[(int)edge];

	public void SetBorder( Edge edge, StyleLength value )
	{
		if ( border[(int)edge] == value ) return;
		border[(int)edge] = value;
		borderMask = WithBit( borderMask, (int)edge, value.IsDefined );
		Changed();
	}

	public StyleLength Gap( Gutter gutter ) => gap[(int)gutter];

	public void SetGap( Gutter gutter, StyleLength value )
	{
		if ( gap[(int)gutter] == value ) return;
		gap[(int)gutter] = value;
		Changed();
	}

	public SizeLength GetDimension( Dimension axis ) => dimensions[(int)axis];

	public void SetDimension( Dimension axis, SizeLength value )
	{
		if ( dimensions[(int)axis] == value ) return;
		dimensions[(int)axis] = value;
		Changed();
	}

	public SizeLength MinDimension( Dimension axis ) => minDimensions[(int)axis];

	public void SetMinDimension( Dimension axis, SizeLength value )
	{
		if ( minDimensions[(int)axis] == value ) return;
		minDimensions[(int)axis] = value;
		minMaxMask = WithBit( minMaxMask, (int)axis, value.IsDefined );
		Changed();
	}

	public SizeLength MaxDimension( Dimension axis ) => maxDimensions[(int)axis];

	public void SetMaxDimension( Dimension axis, SizeLength value )
	{
		if ( maxDimensions[(int)axis] == value ) return;
		maxDimensions[(int)axis] = value;
		minMaxMask = WithBit( minMaxMask, DimensionCount + (int)axis, value.IsDefined );
		Changed();
	}

	//False when neither a minimum nor a maximum is set on the axis, so bounding to them is the identity.
	public bool HasMinOrMaxDimension( Dimension axis ) => (minMaxMask & (0b101u << (int)axis)) != 0;

	//False when no padding and no border is set, so every padding-and-border sum is 0.
	public bool HasPaddingOrBorder => (paddingMask | borderMask) != 0;

	//False when no margin is set, so every margin sum is 0 and no margin is auto.
	public bool HasMargin => marginMask != 0;

	//Take everything from another style, which is what a style copy is.
	public void CopyFrom( LayoutStyle other )
	{
		direction = other.direction;
		flexDirection = other.flexDirection;
		justifyContent = other.justifyContent;
		alignContent = other.alignContent;
		alignItems = other.alignItems;
		alignSelf = other.alignSelf;
		positionType = other.positionType;
		flexWrap = other.flexWrap;
		overflow = other.overflow;
		display = other.display;
		boxSizing = other.boxSizing;

		grid = other.grid?.Clone();

		flex = other.flex;
		flexGrow = other.flexGrow;
		flexShrink = other.flexShrink;
		flexBasis = other.flexBasis;
		aspectRatio = other.aspectRatio;

		margin = other.margin;
		position = other.position;
		padding = other.padding;
		border = other.border;
		gap = other.gap;
		dimensions = other.dimensions;
		minDimensions = other.minDimensions;
		maxDimensions = other.maxDimensions;

		marginMask = other.marginMask;
		positionMask = other.positionMask;
		paddingMask = other.paddingMask;
		borderMask = other.borderMask;
		minMaxMask = other.minMaxMask;

		Changed();
	}

	public bool SameAs( LayoutStyle other )
	{
		if ( direction != other.direction || flexDirection != other.flexDirection ||
			justifyContent != other.justifyContent || alignContent != other.alignContent ||
			alignItems != other.alignItems || alignSelf != other.alignSelf ||
			positionType != other.positionType || flexWrap != other.flexWrap ||
			overflow != other.overflow || display != other.display || boxSizing != other.boxSizing )
			return false;

		if ( !GridStyleValues.Same( grid, other.grid ) ) return false;

		if ( !Num.EqualsOptional( flex, other.flex ) ||
			!Num.EqualsOptional( flexGrow, other.flexGrow ) ||
			!Num.EqualsOptional( flexShrink, other.flexShrink ) ||
			flexBasis != other.flexBasis ||
			!Num.EqualsOptional( aspectRatio, other.aspectRatio ) )
			return false;

		for ( var i = 0; i < EdgeCount; ++i )
		{
			if ( margin[i] != other.margin[i] || position[i] != other.position[i] ||
				padding[i] != other.padding[i] || border[i] != other.border[i] )
				return false;
		}

		for ( var i = 0; i < GutterCount; ++i )
			if ( gap[i] != other.gap[i] ) return false;

		for ( var i = 0; i < DimensionCount; ++i )
		{
			if ( dimensions[i] != other.dimensions[i] || minDimensions[i] != other.minDimensions[i] ||
				maxDimensions[i] != other.maxDimensions[i] )
				return false;
		}

		return true;
	}

	//-- which written edge answers for a physical one ----------------------------

	private static StyleLength LeftEdge( ref EdgeValues edges, uint mask, Direction layout )
	{
		if ( layout == Direction.LTR && Has( mask, Edge.Start ) ) return edges[(int)Edge.Start];
		if ( layout == Direction.RTL && Has( mask, Edge.End ) ) return edges[(int)Edge.End];
		if ( Has( mask, Edge.Left ) ) return edges[(int)Edge.Left];
		if ( Has( mask, Edge.Horizontal ) ) return edges[(int)Edge.Horizontal];

		return edges[(int)Edge.All];
	}

	private static StyleLength TopEdge( ref EdgeValues edges, uint mask )
	{
		if ( Has( mask, Edge.Top ) ) return edges[(int)Edge.Top];
		if ( Has( mask, Edge.Vertical ) ) return edges[(int)Edge.Vertical];

		return edges[(int)Edge.All];
	}

	private static StyleLength RightEdge( ref EdgeValues edges, uint mask, Direction layout )
	{
		if ( layout == Direction.LTR && Has( mask, Edge.End ) ) return edges[(int)Edge.End];
		if ( layout == Direction.RTL && Has( mask, Edge.Start ) ) return edges[(int)Edge.Start];
		if ( Has( mask, Edge.Right ) ) return edges[(int)Edge.Right];
		if ( Has( mask, Edge.Horizontal ) ) return edges[(int)Edge.Horizontal];

		return edges[(int)Edge.All];
	}

	private static StyleLength BottomEdge( ref EdgeValues edges, uint mask )
	{
		if ( Has( mask, Edge.Bottom ) ) return edges[(int)Edge.Bottom];
		if ( Has( mask, Edge.Vertical ) ) return edges[(int)Edge.Vertical];

		return edges[(int)Edge.All];
	}

	//The entry that applies to a physical edge: the most specific one set, per the mask.
	private static StyleLength Of( ref EdgeValues edges, uint mask, PhysicalEdge edge, Direction direction )
	{
		if ( mask == 0 ) return StyleLength.Undefined;

		return edge switch
		{
			PhysicalEdge.Left => LeftEdge( ref edges, mask, direction ),
			PhysicalEdge.Top => TopEdge( ref edges, mask ),
			PhysicalEdge.Right => RightEdge( ref edges, mask, direction ),
			_ => BottomEdge( ref edges, mask ),
		};
	}

	public StyleLength ComputePosition( PhysicalEdge edge, Direction direction ) => Of( ref position, positionMask, edge, direction );
	public StyleLength ComputeMargin( PhysicalEdge edge, Direction direction ) => Of( ref margin, marginMask, edge, direction );
	public StyleLength ComputePadding( PhysicalEdge edge, Direction direction ) => Of( ref padding, paddingMask, edge, direction );
	public StyleLength ComputeBorder( PhysicalEdge edge, Direction direction ) => Of( ref border, borderMask, edge, direction );

	private StyleLength ColumnGap => gap[(int)Gutter.Column].IsDefined ? gap[(int)Gutter.Column] : gap[(int)Gutter.All];
	private StyleLength RowGap => gap[(int)Gutter.Row].IsDefined ? gap[(int)Gutter.Row] : gap[(int)Gutter.All];

	//-- insets -------------------------------------------------------------------

	public bool HorizontalInsetsDefined
		=> Has( positionMask, Edge.Left ) || Has( positionMask, Edge.Right ) || Has( positionMask, Edge.All )
			|| Has( positionMask, Edge.Horizontal ) || Has( positionMask, Edge.Start ) || Has( positionMask, Edge.End );

	public bool VerticalInsetsDefined
		=> Has( positionMask, Edge.Top ) || Has( positionMask, Edge.Bottom ) || Has( positionMask, Edge.All ) || Has( positionMask, Edge.Vertical );

	public bool IsFlexStartPositionDefined( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.FlexStartEdge( axis ), direction ).IsDefined;

	public bool IsFlexStartPositionAuto( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.FlexStartEdge( axis ), direction ).IsAuto;

	public bool IsInlineStartPositionDefined( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.InlineStartEdge( axis, direction ), direction ).IsDefined;

	public bool IsInlineStartPositionAuto( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.InlineStartEdge( axis, direction ), direction ).IsAuto;

	public bool IsFlexEndPositionDefined( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.FlexEndEdge( axis ), direction ).IsDefined;

	public bool IsFlexEndPositionAuto( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.FlexEndEdge( axis ), direction ).IsAuto;

	public bool IsInlineEndPositionDefined( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.InlineEndEdge( axis, direction ), direction ).IsDefined;

	public bool IsInlineEndPositionAuto( FlexDirection axis, Direction direction )
		=> ComputePosition( Axis.InlineEndEdge( axis, direction ), direction ).IsAuto;

	//-- the numbers the algorithm actually asks for -------------------------------

	public float ComputeFlexStartPosition( FlexDirection axis, Direction direction, float axisSize )
		=> Num.OrElse( ComputePosition( Axis.FlexStartEdge( axis ), direction ).Resolve( axisSize ), 0.0f );

	public float ComputeInlineStartPosition( FlexDirection axis, Direction direction, float axisSize )
		=> Num.OrElse( ComputePosition( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( axisSize ), 0.0f );

	public float ComputeFlexEndPosition( FlexDirection axis, Direction direction, float axisSize )
		=> Num.OrElse( ComputePosition( Axis.FlexEndEdge( axis ), direction ).Resolve( axisSize ), 0.0f );

	public float ComputeInlineEndPosition( FlexDirection axis, Direction direction, float axisSize )
		=> Num.OrElse( ComputePosition( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( axisSize ), 0.0f );

	public float ComputeFlexStartMargin( FlexDirection axis, Direction direction, float widthSize )
		=> marginMask == 0 ? 0.0f : Num.OrElse( ComputeMargin( Axis.FlexStartEdge( axis ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeInlineStartMargin( FlexDirection axis, Direction direction, float widthSize )
		=> marginMask == 0 ? 0.0f : Num.OrElse( ComputeMargin( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeFlexEndMargin( FlexDirection axis, Direction direction, float widthSize )
		=> marginMask == 0 ? 0.0f : Num.OrElse( ComputeMargin( Axis.FlexEndEdge( axis ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeInlineEndMargin( FlexDirection axis, Direction direction, float widthSize )
		=> marginMask == 0 ? 0.0f : Num.OrElse( ComputeMargin( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );

	//A border or a padding is never negative, and never undefined at this point.

	public float ComputeFlexStartBorder( FlexDirection axis, Direction direction )
		=> borderMask == 0 ? 0.0f : Num.MaxOrDefined( ComputeBorder( Axis.FlexStartEdge( axis ), direction ).Resolve( 0.0f ), 0.0f );

	public float ComputeInlineStartBorder( FlexDirection axis, Direction direction )
		=> borderMask == 0 ? 0.0f : Num.MaxOrDefined( ComputeBorder( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( 0.0f ), 0.0f );

	public float ComputeFlexEndBorder( FlexDirection axis, Direction direction )
		=> borderMask == 0 ? 0.0f : Num.MaxOrDefined( ComputeBorder( Axis.FlexEndEdge( axis ), direction ).Resolve( 0.0f ), 0.0f );

	public float ComputeInlineEndBorder( FlexDirection axis, Direction direction )
		=> borderMask == 0 ? 0.0f : Num.MaxOrDefined( ComputeBorder( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( 0.0f ), 0.0f );

	public float ComputeFlexStartPadding( FlexDirection axis, Direction direction, float widthSize )
		=> paddingMask == 0 ? 0.0f : Num.MaxOrDefined( ComputePadding( Axis.FlexStartEdge( axis ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeInlineStartPadding( FlexDirection axis, Direction direction, float widthSize )
		=> paddingMask == 0 ? 0.0f : Num.MaxOrDefined( ComputePadding( Axis.InlineStartEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeFlexEndPadding( FlexDirection axis, Direction direction, float widthSize )
		=> paddingMask == 0 ? 0.0f : Num.MaxOrDefined( ComputePadding( Axis.FlexEndEdge( axis ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeInlineEndPadding( FlexDirection axis, Direction direction, float widthSize )
		=> paddingMask == 0 ? 0.0f : Num.MaxOrDefined( ComputePadding( Axis.InlineEndEdge( axis, direction ), direction ).Resolve( widthSize ), 0.0f );

	public float ComputeInlineStartPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize )
		=> HasPaddingOrBorder ? ComputeInlineStartPadding( axis, direction, widthSize ) + ComputeInlineStartBorder( axis, direction ) : 0.0f;

	public float ComputeFlexStartPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize )
		=> HasPaddingOrBorder ? ComputeFlexStartPadding( axis, direction, widthSize ) + ComputeFlexStartBorder( axis, direction ) : 0.0f;

	public float ComputeInlineEndPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize )
		=> HasPaddingOrBorder ? ComputeInlineEndPadding( axis, direction, widthSize ) + ComputeInlineEndBorder( axis, direction ) : 0.0f;

	public float ComputeFlexEndPaddingAndBorder( FlexDirection axis, Direction direction, float widthSize )
		=> HasPaddingOrBorder ? ComputeFlexEndPadding( axis, direction, widthSize ) + ComputeFlexEndBorder( axis, direction ) : 0.0f;

	public float ComputePaddingAndBorderForDimension( Direction direction, Dimension dimension, float widthSize )
	{
		if ( !HasPaddingOrBorder ) return 0.0f;

		var axis = dimension == Dimension.Width ? FlexDirection.Row : FlexDirection.Column;

		return ComputeFlexStartPaddingAndBorder( axis, direction, widthSize )
			+ ComputeFlexEndPaddingAndBorder( axis, direction, widthSize );
	}

	//Border across an axis. Hardcoded left-to-right, because a total does not depend on which way round the two ends are.
	public float ComputeBorderForAxis( FlexDirection axis )
		=> borderMask == 0 ? 0.0f : ComputeInlineStartBorder( axis, Direction.LTR ) + ComputeInlineEndBorder( axis, Direction.LTR );

	//The same for margin.
	public float ComputeMarginForAxis( FlexDirection axis, float widthSize )
		=> marginMask == 0 ? 0.0f : ComputeInlineStartMargin( axis, Direction.LTR, widthSize ) + ComputeInlineEndMargin( axis, Direction.LTR, widthSize );

	public float ComputeGapForAxis( FlexDirection axis, float ownerSize )
	{
		var value = Axis.IsRow( axis ) ? ColumnGap : RowGap;

		return Num.MaxOrDefined( value.Resolve( ownerSize ), 0.0f );
	}

	public float ComputeGapForDimension( Dimension dimension, float ownerSize )
	{
		var value = dimension == Dimension.Width ? ColumnGap : RowGap;

		return Num.MaxOrDefined( value.Resolve( ownerSize ), 0.0f );
	}

	public bool FlexStartMarginIsAuto( FlexDirection axis, Direction direction )
		=> marginMask != 0 && ComputeMargin( Axis.FlexStartEdge( axis ), direction ).IsAuto;

	public bool FlexEndMarginIsAuto( FlexDirection axis, Direction direction )
		=> marginMask != 0 && ComputeMargin( Axis.FlexEndEdge( axis ), direction ).IsAuto;

	public bool InlineStartMarginIsAuto( FlexDirection axis, Direction direction )
		=> marginMask != 0 && ComputeMargin( Axis.InlineStartEdge( axis, direction ), direction ).IsAuto;

	public bool InlineEndMarginIsAuto( FlexDirection axis, Direction direction )
		=> marginMask != 0 && ComputeMargin( Axis.InlineEndEdge( axis, direction ), direction ).IsAuto;

	//-- sizes, once a box-sizing is taken into account -----------------------------

	public float ResolvedMinDimension( Direction direction, Dimension axis, float reference, float ownerWidth )
	{
		var length = minDimensions[(int)axis];
		if ( length.IsUndefined ) return Num.Undefined;

		var value = length.Resolve( reference );
		if ( boxSizing == BoxSizing.BorderBox || Num.IsUndefined( value ) ) return value;

		var extra = ComputePaddingAndBorderForDimension( direction, axis, ownerWidth );

		return value + (Num.IsDefined( extra ) ? extra : 0.0f);
	}

	public float ResolvedMaxDimension( Direction direction, Dimension axis, float reference, float ownerWidth )
	{
		var length = maxDimensions[(int)axis];
		if ( length.IsUndefined ) return Num.Undefined;

		var value = length.Resolve( reference );
		if ( boxSizing == BoxSizing.BorderBox || Num.IsUndefined( value ) ) return value;

		var extra = ComputePaddingAndBorderForDimension( direction, axis, ownerWidth );

		return value + (Num.IsDefined( extra ) ? extra : 0.0f);
	}
}
