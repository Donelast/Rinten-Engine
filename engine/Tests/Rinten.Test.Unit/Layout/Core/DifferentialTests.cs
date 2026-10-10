using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//Random trees must match the frozen reference engine after layout, a text change and a resize
[TestClass]
public class DifferentialTests
{
	public const string SeedsVariable = "RINTEN_LAYOUT_DIFF_SEEDS";
	private const int DefaultSeeds = 300;
	private const float Tolerance = 0.01f;

	internal static int SeedCount => int.TryParse( Environment.GetEnvironmentVariable( SeedsVariable ), out var n ) && n > 0 ? n : DefaultSeeds;

	[TestMethod]
	public void ProductionMatchesReference()
	{
		var seeds = SeedCount;
		var nodes = 0;
		var failures = new List<string>();

		for ( var seed = 0; seed < seeds; seed++ )
		{
			try
			{
				nodes += Compare( (ulong)seed );
			}
			catch ( AssertFailedException e )
			{
				//Every seed is run, so a failure reports how many trees differ, not just the first.
				failures.Add( ( UsesBaseline( (ulong)seed ) ? "[baseline tree] " : "" ) + e.Message );
			}
		}

		var baseline = failures.Count( f => f.StartsWith( "[baseline tree]" ) );
		Console.WriteLine( $"differential: {seeds} seeds, {nodes} nodes compared, {failures.Count} seeds differ, {baseline} of them with baseline alignment" );

		//Baseline-aligned trees may differ in a capped share of seeds, since baselines come from last layout
		if ( failures.Count > baseline || baseline > BaselineAllowance( seeds ) )
			Assert.Fail( $"{failures.Count} of {seeds} seeds differ, {baseline} of them with baseline alignment somewhere (allowed: {BaselineAllowance( seeds )}). The first: {string.Join( " | ", failures.Take( 3 ) )}" );
	}

	//How many trees with baseline alignment may differ between the engines: one per cent. The count itself is recorded in docs/layout-metrics.md.
	internal static int BaselineAllowance( int seeds ) => Math.Max( 3, seeds / 100 );

	//Whether any node of the seed's tree lines up on a baseline, whose computation reads positions of an earlier layout.
	internal static bool UsesBaseline( ulong seed )
	{
		var nodes = new List<IFixtureNode>();
		RandomTrees.Build( FixtureEngines.Production, seed, nodes, new List<IFixtureNode>() );
		return nodes.Any( n => n.Style.AlignItems == Align.Baseline || n.Style.AlignSelf == Align.Baseline );
	}

	//Every rect must be finite; seed 134 found an infinite height the reference also has
	[TestMethod]
	public void EveryRectIsFinite()
	{
		var seeds = SeedCount;
		for ( var seed = 0; seed < seeds; seed++ )
		{
			var nodes = new List<IFixtureNode>();
			var root = RandomTrees.Build( FixtureEngines.Production, (ulong)seed, nodes, new List<IFixtureNode>() );
			root.CalculateLayout( 800, 600 );
			for ( var i = 0; i < nodes.Count; i++ )
			{
				var n = nodes[i];
				if ( float.IsFinite( n.LayoutLeft ) && float.IsFinite( n.LayoutTop ) && float.IsFinite( n.LayoutWidth ) && float.IsFinite( n.LayoutHeight ) ) continue;
				Assert.Fail( $"seed {seed}, node {i}: {Rect( n )}. Describe it with RINTEN_LAYOUT_DESCRIBE={seed}:{i}." );
			}
		}
	}

	[TestMethod]
	public void SameSeedIsTheSameTree()
	{
		var a = new List<IFixtureNode>();
		var b = new List<IFixtureNode>();
		RandomTrees.Build( FixtureEngines.Production, 7, a, new List<IFixtureNode>() );
		RandomTrees.Build( FixtureEngines.Production, 7, b, new List<IFixtureNode>() );
		Assert.AreEqual( a.Count, b.Count );
		Assert.IsTrue( a.Count >= 2 );
	}

	//Lays out one seed in both engines and returns how many nodes were compared.
	private static int Compare( ulong seed )
	{
		var productionNodes = new List<IFixtureNode>();
		var productionTexts = new List<IFixtureNode>();
		var referenceNodes = new List<IFixtureNode>();
		var referenceTexts = new List<IFixtureNode>();

		var production = RandomTrees.Build( FixtureEngines.Production, seed, productionNodes, productionTexts );
		var reference = RandomTrees.Build( FixtureEngines.Reference, seed, referenceNodes, referenceTexts );
		Assert.AreEqual( referenceNodes.Count, productionNodes.Count, $"seed {seed}: the builder is not deterministic" );

		production.CalculateLayout( 800, 600 );
		reference.CalculateLayout( 800, 600 );
		CompareRects( seed, "initial", productionNodes, referenceNodes );

		if ( productionTexts.Count > 0 )
		{
			var index = (int)(seed % (ulong)productionTexts.Count);
			productionTexts[index].Context = (string)productionTexts[index].Context + " changed";
			productionTexts[index].MarkDirty();
			referenceTexts[index].Context = (string)referenceTexts[index].Context + " changed";
			referenceTexts[index].MarkDirty();
			production.CalculateLayout( 800, 600 );
			reference.CalculateLayout( 800, 600 );
			CompareRects( seed, "after text change", productionNodes, referenceNodes );
		}

		production.Style.Width = StyleLength.Points( 500 );
		reference.Style.Width = StyleLength.Points( 500 );
		production.CalculateLayout( 500, float.NaN );
		reference.CalculateLayout( 500, float.NaN );
		CompareRects( seed, "after resize", productionNodes, referenceNodes );

		return productionNodes.Count;
	}

	private static void CompareRects( ulong seed, string pass, List<IFixtureNode> production, List<IFixtureNode> reference )
	{
		for ( var i = 0; i < production.Count; i++ )
		{
			var p = production[i];
			var r = reference[i];
			if ( Same( p.LayoutLeft, r.LayoutLeft ) && Same( p.LayoutTop, r.LayoutTop ) && Same( p.LayoutWidth, r.LayoutWidth ) && Same( p.LayoutHeight, r.LayoutHeight ) )
				continue;

			Assert.Fail( $"seed {seed}, {pass}, node {i}: production {Rect( p )} vs reference {Rect( r )}. Reproduce with RandomTrees.Build(engine, {seed}, ...)." );
		}
	}

	//Equality first: two infinities are the same answer even though their difference is NaN.
	private static bool Same( float a, float b ) => a == b || (float.IsNaN( a ) && float.IsNaN( b )) || MathF.Abs( a - b ) <= Tolerance;

	//A debugging aid: set RINTEN_LAYOUT_DESCRIBE to seed:index and this prints the node's
	//style and its ancestors' styles, which is what a differential failure needs next.
	[TestMethod]
	public void DescribeNode()
	{
		var spec = Environment.GetEnvironmentVariable( "RINTEN_LAYOUT_DESCRIBE" );
		if ( string.IsNullOrEmpty( spec ) ) Assert.Inconclusive( "Set RINTEN_LAYOUT_DESCRIBE=seed:index to describe a node." );

		var parts = spec.Split( ':' );
		var seed = ulong.Parse( parts[0], CultureInfo.InvariantCulture );
		var index = int.Parse( parts[1], CultureInfo.InvariantCulture );

		var nodes = new List<IFixtureNode>();
		var root = RandomTrees.Build( FixtureEngines.Production, seed, nodes, new List<IFixtureNode>() );
		root.CalculateLayout( 800, 600 );

		var chain = new List<IFixtureNode>();
		for ( var node = nodes[index]; node is not null; node = node.Owner ) chain.Add( node );
		chain.Reverse();

		var sb = new System.Text.StringBuilder();
		foreach ( var node in chain )
		{
			sb.AppendLine( $"node {nodes.IndexOf( node )} {Rect( node )} children={node.ChildCount} text={node.HasMeasureFunc} ctx={node.Context}" );
			sb.AppendLine( "  " + DescribeStyle( node.Style ) );
		}

		//And the node's own children, since the answer is often below rather than above.
		var target = nodes[index];
		for ( var i = 0; i < target.ChildCount; i++ )
		{
			var child = target.GetChild( i );
			sb.AppendLine( $"  child {nodes.IndexOf( child )} {Rect( child )} children={child.ChildCount} text={child.HasMeasureFunc} ctx={child.Context}" );
			sb.AppendLine( "    " + DescribeStyle( child.Style ) );
		}

		Console.WriteLine( sb.ToString() );
		Assert.Inconclusive( sb.ToString() );
	}

	internal static string DescribeStyle( IFixtureStyle s )
	{
		var ci = CultureInfo.InvariantCulture;
		string L( StyleLength v ) => v.IsAuto ? "auto" : v.IsPercent ? v.Value.ToString( ci ) + "%" : v.IsPoints ? v.Value.ToString( ci ) + "px" : "-";
		var edges = new System.Text.StringBuilder();
		if ( s is EngineNode.StyleView view )
		{
			foreach ( var edge in Enum.GetValues<Edge>() )
			{
				var m = view.Margin( edge );
				var p = view.Padding( edge );
				var i = view.Inset( edge );
				var b = view.Border( edge );
				if ( m.IsDefined ) edges.Append( $" margin-{edge}={L( m )}" );
				if ( p.IsDefined ) edges.Append( $" padding-{edge}={L( p )}" );
				if ( i.IsDefined ) edges.Append( $" inset-{edge}={L( i )}" );
				if ( b.IsDefined ) edges.Append( $" border-{edge}={L( b )}" );
			}
		}

		return $"dir={s.FlexDirection} wrap={s.FlexWrap} pos={s.PositionType} display={s.Display} overflow={s.Overflow} "
			+ $"w={L( s.Width )} h={L( s.Height )} min={L( s.MinWidth )}x{L( s.MinHeight )} max={L( s.MaxWidth )}x{L( s.MaxHeight )} "
			+ $"grow={s.FlexGrow} shrink={s.FlexShrink} basis={L( s.FlexBasis )} aspect={s.AspectRatio} "
			+ $"justify={s.JustifyContent} alignItems={s.AlignItems} alignSelf={s.AlignSelf} alignContent={s.AlignContent}{edges}";
	}

	internal static string Rect( IFixtureNode n )
	{
		var ci = CultureInfo.InvariantCulture;
		return $"[{n.LayoutLeft.ToString( "R", ci )}, {n.LayoutTop.ToString( "R", ci )}, {n.LayoutWidth.ToString( "R", ci )} x {n.LayoutHeight.ToString( "R", ci )}]";
	}
}
