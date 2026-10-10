using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//Debug aid: RINTEN_LAYOUT_TRACE counts per-node cache misses and constraint shapes for a scenario
[TestClass]
public class VisitTraceTests
{
	private sealed class Visit
	{
		public int Count;
		public readonly HashSet<string> Questions = new();
	}

	[TestMethod]
	public void TraceScenario()
	{
		var scenario = Environment.GetEnvironmentVariable( "RINTEN_LAYOUT_TRACE" );
		if ( string.IsNullOrEmpty( scenario ) ) Assert.Inconclusive( "Set RINTEN_LAYOUT_TRACE=<deep-tree scenario> to trace it." );

		var all = new List<IFixtureNode>();
		IFixtureNode root;
		float rootWidth, rootHeight;
		var direction = Direction.LTR;

		//The flat benchmark shapes are traced too; RINTEN_LAYOUT_ABLATE=reuse switches the container reuse off.
		if ( scenario is "widerow" or "inspector" )
			root = TreeShapes.Build( FixtureEngines.Production, scenario, all, new List<IFixtureNode>(), out rootWidth, out rootHeight );
		else
			root = DeepTrees.Build( FixtureEngines.Production, scenario, all, out _, out rootWidth, out rootHeight, out direction );

		var ablate = Environment.GetEnvironmentVariable( "RINTEN_LAYOUT_ABLATE" ) ?? "";
		FlexLayout.DisableContainerReuse = ablate == "reuse";

		var visits = new Dictionary<LayoutNode, Visit>();
		var shapes = new Dictionary<string, int>();
		var byDepth = new Dictionary<int, int>();
		var total = 0;

		FlexLayout.Trace = ( node, w, wm, h, hm, layout, depth ) =>
		{
			total++;
			if ( !visits.TryGetValue( node, out var v ) ) visits[node] = v = new Visit();
			v.Count++;
			v.Questions.Add( FormattableString.Invariant( $"{wm}:{w:R} {hm}:{h:R} {(layout ? "L" : "M")}" ) );

			var shape = $"{(layout ? "layout " : "measure")} w={wm} h={hm}" + (node.HasMeasureFunc ? " leaf" : node.ChildCount == 0 ? " empty" : " container");
			shapes[shape] = shapes.GetValueOrDefault( shape ) + 1;
			byDepth[depth] = byDepth.GetValueOrDefault( depth ) + 1;
		};

		try
		{
			FixtureCounters.MeasureCalls = 0;
			root.CalculateLayout( rootWidth, rootHeight, direction );
		}
		finally
		{
			FlexLayout.Trace = null;
			FlexLayout.DisableContainerReuse = false;
		}

		var sb = new StringBuilder();
		sb.AppendLine( $"{scenario}: {all.Count} nodes, {total} visits past the cache, {FixtureCounters.MeasureCalls} measure calls" );

		sb.AppendLine( "by shape:" );
		foreach ( var (shape, count) in shapes.OrderByDescending( p => p.Value ) )
			sb.AppendLine( $"  {count,8}  {shape}" );

		sb.AppendLine( "by depth:" );
		foreach ( var (depth, count) in byDepth.OrderBy( p => p.Key ) )
			sb.AppendLine( $"  {depth,3}: {count}" );

		sb.AppendLine( "most visited nodes (visits / distinct questions):" );
		foreach ( var (node, v) in visits.OrderByDescending( p => p.Value.Count ).Take( 8 ) )
		{
			var index = all.FindIndex( n => ReferenceEquals( ((EngineNode)n).Node, node ) );
			sb.AppendLine( $"  node {index} {(node.HasMeasureFunc ? "leaf" : "container")} {v.Count} / {v.Questions.Count}" );
			foreach ( var q in v.Questions.Take( 12 ) ) sb.AppendLine( $"      {q}" );
		}

		Console.WriteLine( sb.ToString() );
		Assert.Inconclusive( sb.ToString() );
	}
}
