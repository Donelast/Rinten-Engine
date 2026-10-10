using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Test-time instrumentation of the cache. Nothing here runs in production: the
//trace is a null delegate and the verifier a false flag, one check each per visit.
internal static partial class FlexLayout
{
	//Every question the cache could not answer: the node, the constraint it was
	//asked under, whether a layout or a measurement was wanted, and how deep it sits.
	internal delegate void TraceVisit( LayoutNode node, float availableWidth, SizingMode widthMode,
		float availableHeight, SizingMode heightMode, bool performLayout, int depth );

	[ThreadStatic] internal static TraceVisit Trace;

	//A switch for measuring what the container reuse is worth: with it set, the engine
	//behaves as it did without it. Test-time only; the results must not change either way.
	[ThreadStatic] internal static bool DisableContainerReuse;

	//While set, every container answer taken from the cache is recomputed and compared, and each disagreement is listed in
	//VerifyMismatches. This is how the reuse rules are checked against the algorithm itself rather than against an argument about it.
	[ThreadStatic] internal static bool VerifyCache;

	[ThreadStatic] internal static List<string> VerifyMismatches;

	//How many recomputations may nest: one by default, more where a tree is shallow enough to afford it.
	[ThreadStatic] internal static int VerifyNesting;

	//What the test is doing, for the mismatch messages: a seed, a scenario.
	[ThreadStatic] internal static string VerifyContext;

	[ThreadStatic] private static int verifyingDepth;

	private static void VerifyHit( LayoutNode node, in CachedMeasurement served, bool isLayoutEntry, bool probed,
		float availableWidth, SizingMode widthMode, float availableHeight, SizingMode heightMode,
		Direction ownerDirection, float ownerWidth, float ownerHeight, bool performLayout, int depth, uint generationCount )
	{
		//The recomputation asks the children again; their own hits are not verified in turn, or a deep tree would be recomputed once per level.
		if ( verifyingDepth >= Math.Max( 1, VerifyNesting ) ) return;

		//Two limits within a ten-thousandth of each other are the same question to the cache, by a tolerance the original chose; an answer
		//that jumps between them (a wrap that just fits or just does not) is not a reuse rule's doing, so such a hit is left alone.
		if ( served.WidthSizingMode == widthMode && served.HeightSizingMode == heightMode
			&& !( served.AvailableWidth == availableWidth || ( Num.IsUndefined( served.AvailableWidth ) && Num.IsUndefined( availableWidth ) ) )
			|| served.WidthSizingMode == widthMode && served.HeightSizingMode == heightMode
			&& !( served.AvailableHeight == availableHeight || ( Num.IsUndefined( served.AvailableHeight ) && Num.IsUndefined( availableHeight ) ) ) )
			return;

		var cachedWidth = served.ComputedWidth;
		var cachedHeight = served.ComputedHeight;

		var before = recent is null ? "" : string.Join( "\n", recent );
		recent?.Clear();

		verifyingDepth++;
		try
		{
			CalculateLayoutImpl( node, availableWidth, availableHeight, ownerDirection,
				widthMode, heightMode, ownerWidth, ownerHeight, performLayout, depth, generationCount );
		}
		finally
		{
			verifyingDepth--;
		}

		var realWidth = node.Layout.MeasuredDimension( Dimension.Width );
		var realHeight = node.Layout.MeasuredDimension( Dimension.Height );

		if ( Close( cachedWidth, realWidth ) && Close( cachedHeight, realHeight ) ) return;

		var during = recent is null ? "" : string.Join( "\n", recent );

		VerifyMismatches ??= new List<string>();
		VerifyMismatches.Add( $"[{VerifyContext}] depth {depth} {Describe( node )} asked w={widthMode}:{availableWidth} h={heightMode}:{availableHeight} owner={ownerWidth}x{ownerHeight} layout={performLayout}: "
			+ $"cached {cachedWidth} x {cachedHeight}, real {realWidth} x {realHeight}; "
			+ $"entry w={served.WidthSizingMode}:{served.AvailableWidth} h={served.HeightSizingMode}:{served.AvailableHeight} owner={served.OwnerWidth}x{served.OwnerHeight} layout={isLayoutEntry} probe={probed}"
			+ DescribeChildren( node )
			+ "\n  visits before:\n" + before + "\n  visits during the recomputation:\n" + during );
	}

	//The last visits, kept while the verifier is on so a mismatch can show what the children answered.
	[ThreadStatic] private static List<string> recent;
	private const int RecentLimit = 80;

	private static void Note( LayoutNode node, int depth, float availableWidth, SizingMode widthMode, float availableHeight, SizingMode heightMode,
		bool performLayout, bool hit, bool probed, bool isLayoutEntry, int cachedIndex, ref LayoutResults layout )
	{
		recent ??= new List<string>();
		if ( recent.Count >= RecentLimit ) recent.RemoveAt( 0 );

		var id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( node ) % 10000;
		var kind = node.HasMeasureFunc ? "text" : $"{node.Style.FlexDirection}/{node.ChildCount}";
		var how = hit ? ( isLayoutEntry ? "hit layout" : $"hit entry {cachedIndex}" ) : probed ? "probe+computed" : "computed";
		recent.Add( $"    {new string( ' ', depth )}#{id} {kind} w={widthMode}:{availableWidth} h={heightMode}:{availableHeight} {(performLayout ? "L" : "M")} {how} -> {layout.MeasuredDimension( Dimension.Width )} x {layout.MeasuredDimension( Dimension.Height )}" );
	}

	//A thousandth, relative: two paths through float arithmetic may differ in the last place.
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static bool Close( float a, float b )
		=> Num.InexactEquals( a, b ) || MathF.Abs( a - b ) <= 0.001f * MathF.Max( 1f, MathF.Abs( a ) );

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static string Describe( LayoutNode node )
	{
		var s = node.Style;
		return $"[{s.FlexDirection} {s.FlexWrap} justify={s.JustifyContent} items={s.AlignItems} self={s.AlignSelf} pos={s.PositionType} "
			+ $"w={s.GetDimension( Dimension.Width )} h={s.GetDimension( Dimension.Height )} min={s.MinDimension( Dimension.Width )}x{s.MinDimension( Dimension.Height )} max={s.MaxDimension( Dimension.Width )}x{s.MaxDimension( Dimension.Height )} "
			+ $"grow={s.FlexGrow} shrink={s.FlexShrink} basis={s.FlexBasis} aspect={s.AspectRatio} overflow={s.Overflow} text={node.HasMeasureFunc} children={node.ChildCount} blocks={node.SubtreeBlocksReuse} percent={node.SubtreeUsesPercent}]";
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	private static string DescribeChildren( LayoutNode node )
	{
		var sb = new System.Text.StringBuilder();
		foreach ( var child in node.RawChildren )
		{
			sb.Append( "\n    child " ).Append( Describe( child ) )
				.Append( $" line={child.LineIndex} top={child.Layout.Position( PhysicalEdge.Top )} measured={child.Layout.MeasuredDimension( Dimension.Width )}x{child.Layout.MeasuredDimension( Dimension.Height )} baseline={child.Layout.Baseline}" );
		}
		return sb.ToString();
	}
}
