//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
//Stub for production Diagnostics.cs: every hook is empty so nothing leaks into the reference
using System.Collections.Generic;

namespace Rinten.Layout.Reference;

internal static partial class FlexLayout
{
	internal delegate void TraceVisit( LayoutNode node, float availableWidth, SizingMode widthMode,
		float availableHeight, SizingMode heightMode, bool performLayout, int depth );

	internal static readonly TraceVisit Trace = null;

	internal static readonly bool DisableContainerReuse = false;

	internal static readonly bool VerifyCache = false;

	private static void VerifyHit( LayoutNode node, in CachedMeasurement served, bool isLayoutEntry, bool probed,
		float availableWidth, SizingMode widthMode, float availableHeight, SizingMode heightMode,
		Direction ownerDirection, float ownerWidth, float ownerHeight, bool performLayout, int depth, uint generationCount )
	{
	}

	private static void Note( LayoutNode node, int depth, float availableWidth, SizingMode widthMode, float availableHeight, SizingMode heightMode,
		bool performLayout, bool hit, bool probed, bool isLayoutEntry, int cachedIndex, ref LayoutResults layout )
	{
	}
}
