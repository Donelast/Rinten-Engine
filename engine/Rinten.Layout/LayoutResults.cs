using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//One answer a node gave for one question about its size. The comparison is not a plain field-by-field one: an undefined
//on both sides counts as a match, which is what lets a pass that asked for "whatever you like" reuse an earlier answer.
internal struct CachedMeasurement
{
	public float AvailableWidth;
	public float AvailableHeight;
	public SizingMode WidthSizingMode;
	public SizingMode HeightSizingMode;

	/// <summary>What percentages resolved against when the answer was computed. Only compared where the subtree has any.</summary>
	public float OwnerWidth;
	public float OwnerHeight;

	public float ComputedWidth;
	public float ComputedHeight;

	/// <summary>The baseline that went with the answer, restored with it.</summary>
	public float Baseline;


	/// <summary>Whether the content overflowed, so a cached answer restores the flag along with the size.</summary>
	public bool HadOverflow;

	/// <summary>What a slot holds before anything was measured into it.</summary>
	public static CachedMeasurement Empty => new()
	{
		AvailableWidth = -1,
		AvailableHeight = -1,
		WidthSizingMode = SizingMode.MaxContent,
		HeightSizingMode = SizingMode.MaxContent,
		OwnerWidth = Num.Undefined,
		OwnerHeight = Num.Undefined,
		ComputedWidth = -1,
		ComputedHeight = -1,
		Baseline = Num.Undefined,
	};

	/// <summary>Whether the owner size this was computed against is the one in force now.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly bool SameOwner( float ownerWidth, float ownerHeight )
		=> Num.InexactEquals( OwnerWidth, ownerWidth ) && Num.InexactEquals( OwnerHeight, ownerHeight );

	public readonly bool SameAs( in CachedMeasurement other )
	{
		var equal = WidthSizingMode == other.WidthSizingMode && HeightSizingMode == other.HeightSizingMode;

		if ( Num.IsDefined( AvailableWidth ) || Num.IsDefined( other.AvailableWidth ) )
			equal = equal && AvailableWidth == other.AvailableWidth;

		if ( Num.IsDefined( AvailableHeight ) || Num.IsDefined( other.AvailableHeight ) )
			equal = equal && AvailableHeight == other.AvailableHeight;

		if ( Num.IsDefined( ComputedWidth ) || Num.IsDefined( other.ComputedWidth ) )
			equal = equal && ComputedWidth == other.ComputedWidth;

		if ( Num.IsDefined( ComputedHeight ) || Num.IsDefined( other.ComputedHeight ) )
			equal = equal && ComputedHeight == other.ComputedHeight;

		return equal;
	}
}

//The measurement cache: a fixed number of slots living inside the results, not on the heap.
[InlineArray( LayoutResults.MaxCachedMeasurements )]
internal struct CachedMeasurementBuffer
{
	private CachedMeasurement element0;
}

//A node's layout results and measurement cache, held inline; always take it as ref var
internal struct LayoutResults
{
	public const int MaxCachedMeasurements = 8;

	public uint ComputedFlexBasisGeneration;
	public float ComputedFlexBasis;

	/// <summary>The floor a flex item keeps along the main axis, from CSS Flexbox's automatic minimum size. Undefined where none applies - which, with the default errata, is everywhere the author gave no explicit minimum.</summary>
	public float ComputedAutoMinMainSize;

	public uint GenerationCount;
	public uint ConfigVersion;
	public Direction LastOwnerDirection;

	public int NextCachedMeasurementsIndex;
	public CachedMeasurementBuffer CachedMeasurements;
	public CachedMeasurement CachedLayout;

	public Direction Direction;
	public bool HadOverflow;

	/// <summary>Where a box's text sits, as an offset down from its top: computed at the end of its layout from where its children were put, so a later measurement of a descendant cannot move it.</summary>
	public float Baseline;


	private float width, height;
	private float measuredWidth, measuredHeight;
	private float rawWidth, rawHeight;
	private float left, top, right, bottom;
	private float marginLeft, marginTop, marginRight, marginBottom;
	private float borderLeft, borderTop, borderRight, borderBottom;
	private float paddingLeft, paddingTop, paddingRight, paddingBottom;

	/// <summary>What a node has before it has ever been laid out.</summary>
	public static LayoutResults Create()
	{
		LayoutResults results = default;
		results.ComputedFlexBasis = Num.Undefined;
		results.ComputedAutoMinMainSize = Num.Undefined;
		results.Baseline = Num.Undefined;
		results.LastOwnerDirection = Direction.Inherit;
		results.Direction = Direction.Inherit;
		for ( var i = 0; i < MaxCachedMeasurements; ++i ) results.CachedMeasurements[i] = CachedMeasurement.Empty;
		results.CachedLayout = CachedMeasurement.Empty;
		results.width = results.height = Num.Undefined;
		results.measuredWidth = results.measuredHeight = Num.Undefined;
		results.rawWidth = results.rawHeight = Num.Undefined;
		return results;
	}

	/// <summary>Back to what a node has before it has ever been laid out.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Reset() => this = Create();

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float GetDimension( Dimension axis ) => axis == Dimension.Width ? width : height;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetDimension( Dimension axis, float value )
	{
		if ( axis == Dimension.Width ) width = value;
		else height = value;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float MeasuredDimension( Dimension axis ) => axis == Dimension.Width ? measuredWidth : measuredHeight;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetMeasuredDimension( Dimension axis, float value )
	{
		if ( axis == Dimension.Width ) measuredWidth = value;
		else measuredHeight = value;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float RawDimension( Dimension axis ) => axis == Dimension.Width ? rawWidth : rawHeight;

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetRawDimension( Dimension axis, float value )
	{
		if ( axis == Dimension.Width ) rawWidth = value;
		else rawHeight = value;
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Position( PhysicalEdge edge ) => edge switch
	{
		PhysicalEdge.Left => left,
		PhysicalEdge.Top => top,
		PhysicalEdge.Right => right,
		_ => bottom,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetPosition( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left: left = value; break;
			case PhysicalEdge.Top: top = value; break;
			case PhysicalEdge.Right: right = value; break;
			default: bottom = value; break;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Margin( PhysicalEdge edge ) => edge switch
	{
		PhysicalEdge.Left => marginLeft,
		PhysicalEdge.Top => marginTop,
		PhysicalEdge.Right => marginRight,
		_ => marginBottom,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetMargin( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left: marginLeft = value; break;
			case PhysicalEdge.Top: marginTop = value; break;
			case PhysicalEdge.Right: marginRight = value; break;
			default: marginBottom = value; break;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Border( PhysicalEdge edge ) => edge switch
	{
		PhysicalEdge.Left => borderLeft,
		PhysicalEdge.Top => borderTop,
		PhysicalEdge.Right => borderRight,
		_ => borderBottom,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetBorder( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left: borderLeft = value; break;
			case PhysicalEdge.Top: borderTop = value; break;
			case PhysicalEdge.Right: borderRight = value; break;
			default: borderBottom = value; break;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public readonly float Padding( PhysicalEdge edge ) => edge switch
	{
		PhysicalEdge.Left => paddingLeft,
		PhysicalEdge.Top => paddingTop,
		PhysicalEdge.Right => paddingRight,
		_ => paddingBottom,
	};

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void SetPadding( PhysicalEdge edge, float value )
	{
		switch ( edge )
		{
			case PhysicalEdge.Left: paddingLeft = value; break;
			case PhysicalEdge.Top: paddingTop = value; break;
			case PhysicalEdge.Right: paddingRight = value; break;
			default: paddingBottom = value; break;
		}
	}
}
