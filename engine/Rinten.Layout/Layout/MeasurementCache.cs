using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Whether a measurement from a previous pass still answers the current question (Yoga's Cache.cpp)
internal static class MeasurementCache
{
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool SizeIsExactAndMatchesOldMeasuredSize( SizingMode mode, float size, float lastComputedSize )
		=> mode == SizingMode.StretchFit && Num.InexactEquals( size, lastComputedSize );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool OldSizeIsMaxContentAndStillFits( SizingMode mode, float size, SizingMode lastMode, float lastComputedSize )
		=> mode == SizingMode.FitContent && lastMode == SizingMode.MaxContent
			&& ( size >= lastComputedSize || Num.InexactEquals( size, lastComputedSize ) );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool NewSizeIsStricterAndStillValid( SizingMode mode, float size, SizingMode lastMode,
		float lastSize, float lastComputedSize )
		=> lastMode == SizingMode.FitContent && mode == SizingMode.FitContent
			&& Num.IsDefined( lastSize ) && Num.IsDefined( size ) && Num.IsDefined( lastComputedSize )
			&& lastSize > size
			&& ( lastComputedSize <= size || Num.InexactEquals( size, lastComputedSize ) );

	public static bool CanUseCachedMeasurement(
		SizingMode widthMode, float availableWidth,
		SizingMode heightMode, float availableHeight,
		SizingMode lastWidthMode, float lastAvailableWidth,
		SizingMode lastHeightMode, float lastAvailableHeight,
		float lastComputedWidth, float lastComputedHeight,
		float marginRow, float marginColumn,
		LayoutConfig config )
	{
		if ( ( Num.IsDefined( lastComputedHeight ) && lastComputedHeight < 0 ) ||
			( Num.IsDefined( lastComputedWidth ) && lastComputedWidth < 0 ) )
			return false;

		var pointScaleFactor = config.PointScaleFactor;

		//With snapping on, two sizes that land on the same pixel are the same question.
		//With it off - which is what the interface sets - they are compared as they are.
		var rounded = pointScaleFactor != 0;

		var effectiveWidth = rounded
			? PixelGrid.RoundValueToPixelGrid( availableWidth, pointScaleFactor, false, false )
			: availableWidth;

		var effectiveHeight = rounded
			? PixelGrid.RoundValueToPixelGrid( availableHeight, pointScaleFactor, false, false )
			: availableHeight;

		var effectiveLastWidth = rounded
			? PixelGrid.RoundValueToPixelGrid( lastAvailableWidth, pointScaleFactor, false, false )
			: lastAvailableWidth;

		var effectiveLastHeight = rounded
			? PixelGrid.RoundValueToPixelGrid( lastAvailableHeight, pointScaleFactor, false, false )
			: lastAvailableHeight;

		var sameWidthSpec = lastWidthMode == widthMode && Num.InexactEquals( effectiveLastWidth, effectiveWidth );
		var sameHeightSpec = lastHeightMode == heightMode && Num.InexactEquals( effectiveLastHeight, effectiveHeight );

		//Both limits are taken without the margin: the original compared the old one with it and
		//the new one without, and a question looser by less than the margin passed as stricter.
		var widthIsCompatible = sameWidthSpec
			|| SizeIsExactAndMatchesOldMeasuredSize( widthMode, availableWidth - marginRow, lastComputedWidth )
			|| OldSizeIsMaxContentAndStillFits( widthMode, availableWidth - marginRow, lastWidthMode, lastComputedWidth )
			|| NewSizeIsStricterAndStillValid( widthMode, availableWidth - marginRow, lastWidthMode, lastAvailableWidth - marginRow, lastComputedWidth );

		var heightIsCompatible = sameHeightSpec
			|| SizeIsExactAndMatchesOldMeasuredSize( heightMode, availableHeight - marginColumn, lastComputedHeight )
			|| OldSizeIsMaxContentAndStillFits( heightMode, availableHeight - marginColumn, lastHeightMode, lastComputedHeight )
			|| NewSizeIsStricterAndStillValid( heightMode, availableHeight - marginColumn, lastHeightMode, lastAvailableHeight - marginColumn, lastComputedHeight );

		return widthIsCompatible && heightIsCompatible;
	}

	/// <summary>Cache check for a box with children: only answers that fit or had no limit are reusable</summary>
	public static bool CanUseCachedContainerMeasurement(
		SizingMode widthMode, float availableWidth,
		SizingMode heightMode, float availableHeight,
		SizingMode lastWidthMode, float lastAvailableWidth,
		SizingMode lastHeightMode, float lastAvailableHeight,
		float lastComputedWidth, float lastComputedHeight,
		float marginRow, float marginColumn,
		LayoutConfig config )
	{
		if ( ( Num.IsDefined( lastComputedHeight ) && lastComputedHeight < 0 ) ||
			( Num.IsDefined( lastComputedWidth ) && lastComputedWidth < 0 ) )
			return false;

		var pointScaleFactor = config.PointScaleFactor;
		var rounded = pointScaleFactor != 0;

		var effectiveWidth = rounded ? PixelGrid.RoundValueToPixelGrid( availableWidth, pointScaleFactor, false, false ) : availableWidth;
		var effectiveHeight = rounded ? PixelGrid.RoundValueToPixelGrid( availableHeight, pointScaleFactor, false, false ) : availableHeight;
		var effectiveLastWidth = rounded ? PixelGrid.RoundValueToPixelGrid( lastAvailableWidth, pointScaleFactor, false, false ) : lastAvailableWidth;
		var effectiveLastHeight = rounded ? PixelGrid.RoundValueToPixelGrid( lastAvailableHeight, pointScaleFactor, false, false ) : lastAvailableHeight;

		var sameWidthSpec = lastWidthMode == widthMode && Num.InexactEquals( effectiveLastWidth, effectiveWidth );
		var sameHeightSpec = lastHeightMode == heightMode && Num.InexactEquals( effectiveLastHeight, effectiveHeight );

		if ( sameWidthSpec && sameHeightSpec ) return true;

		var width = availableWidth - marginRow;
		var height = availableHeight - marginColumn;
		var lastWidth = lastAvailableWidth - marginRow;
		var lastHeight = lastAvailableHeight - marginColumn;

		//An answer that overflowed on either axis was computed with the children squeezed, and what they
		//did under the squeeze depends on the other axis too: it answers nothing but the very same question.
		if ( Overflowed( lastWidthMode, lastWidth, lastComputedWidth ) || Overflowed( lastHeightMode, lastHeight, lastComputedHeight ) )
			return false;

		if ( ( Settled( widthMode, width ) && Settled( heightMode, height ) )
			|| ( Settled( lastWidthMode, lastWidth ) && Settled( lastHeightMode, lastHeight ) ) )
			return false;

		var widthIsCompatible = sameWidthSpec
			|| ( ContentBased( lastWidthMode, lastWidth, lastComputedWidth ) && SizeIsExactAndMatchesOldMeasuredSize( widthMode, width, lastComputedWidth ) )
			|| OldSizeIsMaxContentAndStillFits( widthMode, width, lastWidthMode, lastComputedWidth )
			|| NewSizeIsStricterAndStillValid( widthMode, width, lastWidthMode, lastWidth, lastComputedWidth );

		var heightIsCompatible = sameHeightSpec
			|| ( ContentBased( lastHeightMode, lastHeight, lastComputedHeight ) && SizeIsExactAndMatchesOldMeasuredSize( heightMode, height, lastComputedHeight ) )
			|| OldSizeIsMaxContentAndStillFits( heightMode, height, lastHeightMode, lastComputedHeight )
			|| NewSizeIsStricterAndStillValid( heightMode, height, lastHeightMode, lastHeight, lastComputedHeight );

		return widthIsCompatible && heightIsCompatible;
	}

	//An axis the constraints settle on their own: an exact size, or "at most" nothing.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool Settled( SizingMode mode, float size )
		=> mode == SizingMode.StretchFit || ( mode == SizingMode.FitContent && Num.IsDefined( size ) && size <= 0 );

	//An "at most" answer that came out over its limit.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool Overflowed( SizingMode mode, float size, float computed )
		=> mode == SizingMode.FitContent && Num.IsDefined( size ) && computed > size && !Num.InexactEquals( computed, size );

	//An answer the content decided: taken with no limit, or under a limit it kept within.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool ContentBased( SizingMode mode, float size, float computed )
		=> mode == SizingMode.MaxContent
			|| ( mode == SizingMode.FitContent && ( computed <= size || Num.InexactEquals( computed, size ) ) );
}
