using System;
using System.Collections.Generic;
using System.Text;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//The deterministic numbers that go into docs/layout-metrics.md: how much a tree costs to build, how many times measure functions run per pass,
//how many bytes a pass allocates. Printed, not asserted, except for the sanity floor on node counts. Timings belong to the benchmark project.
[TestClass]
public class LayoutMetricsTests
{
	public TestContext TestContext { get; set; }

	[TestMethod]
	[DataRow( "deeptree:plain" )]
	[DataRow( "deeptree:mixed" )]
	[DataRow( "deeptree:grow" )]
	[DataRow( "deeptree:block" )]
	[DataRow( "deeptree:grid" )]
	[DataRow( "widerow" )]
	[DataRow( "inspector" )]
	public void Record( string shape )
	{
		var sb = new StringBuilder();
		void Line( string metric, long value ) => sb.Append( "metric " ).Append( shape ).Append( '.' ).Append( metric ).Append( '=' ).Append( value ).Append( '\n' );

		var all = new List<IFixtureNode>();
		var texts = new List<IFixtureNode>();
		float width, height;

		var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
		var root = TreeShapes.Build( FixtureEngines.Production, shape, all, texts, out width, out height );
		var buildBytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
		Assert.IsTrue( all.Count > 30, $"{shape}: only {all.Count} nodes" );

		Line( "nodes", all.Count );
		Line( "text_leaves", texts.Count );
		Line( "build_bytes_per_node", buildBytes / all.Count );

		Measure( sb, shape, "initial", () => root.CalculateLayout( width, height ) );

		//One deep text leaf changes: the path to the root is dirty, everything else answers from cache.
		var leaf = texts[texts.Count / 2];
		Measure( sb, shape, "leaf_relayout", () =>
		{
			leaf.Context = (string)leaf.Context + " changed";
			leaf.MarkDirty();
			root.CalculateLayout( width, height );
		} );

		//Nothing is dirty; the root's constraint changes and every cache entry is consulted.
		Measure( sb, shape, "resize", () => root.CalculateLayout( width - 137, float.NaN ) );

		//Everything is dirty, as after a stylesheet change: the whole tree is recomputed on a warm heap.
		Measure( sb, shape, "all_dirty", () =>
		{
			foreach ( var node in all ) node.MarkDirty();
			root.CalculateLayout( width, height );
		} );

		TestContext.WriteLine( sb.ToString() );
	}

	private static void Measure( StringBuilder sb, string shape, string pass, Action layout )
	{
		FixtureCounters.MeasureCalls = 0;
		var bytesBefore = GC.GetAllocatedBytesForCurrentThread();
		layout();
		var bytes = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
		sb.Append( "metric " ).Append( shape ).Append( '.' ).Append( pass ).Append( "_measure_calls=" ).Append( FixtureCounters.MeasureCalls ).Append( '\n' );
		sb.Append( "metric " ).Append( shape ).Append( '.' ).Append( pass ).Append( "_bytes=" ).Append( bytes ).Append( '\n' );
	}
}

//The tree shapes the metrics and the benchmarks agree on.
internal static class TreeShapes
{
	public static IFixtureNode Build( IFixtureEngine engine, string shape, List<IFixtureNode> all, List<IFixtureNode> texts, out float width, out float height )
	{
		if ( shape.StartsWith( "deeptree:" ) )
		{
			var root = DeepTrees.Build( engine, shape.Substring( "deeptree:".Length ), all, out _, out width, out height, out _ );
			texts.AddRange( all.Where( n => n.HasMeasureFunc ) );
			return root;
		}

		return shape switch
		{
			"widerow" => WideRow( engine, all, texts, out width, out height ),
			"inspector" => Inspector( engine, all, texts, out width, out height ),
			_ => throw new ArgumentException( shape ),
		};
	}

	private static IFixtureNode Text( IFixtureEngine engine, List<IFixtureNode> all, List<IFixtureNode> texts, Rng rng, int words )
	{
		var node = engine.CreateUiNode();
		node.Context = TextMeasure.Words( rng, words );
		node.MeasureFunc = TextMeasure.Measure;
		all.Add( node );
		texts.Add( node );
		return node;
	}

	//Twenty wrapping rows of fifty text leaves each: wide, shallow, wrap-heavy.
	private static IFixtureNode WideRow( IFixtureEngine engine, List<IFixtureNode> all, List<IFixtureNode> texts, out float width, out float height )
	{
		width = 1200;
		height = 800;
		var rng = new Rng( 11 );
		var root = engine.CreateUiNode();
		root.Style.FlexDirection = FlexDirection.Column;
		all.Add( root );

		for ( var r = 0; r < 20; r++ )
		{
			var row = engine.CreateUiNode();
			row.Style.FlexDirection = FlexDirection.Row;
			row.Style.FlexWrap = Wrap.Wrap;
			row.Style.SetGap( Gutter.All, StyleLength.Points( 4 ) );
			row.Style.SetPadding( Edge.All, StyleLength.Points( 2 ) );
			all.Add( row );
			root.AddChild( row );
			for ( var i = 0; i < 50; i++ )
			{
				var text = Text( engine, all, texts, rng, rng.Between( 1, 3 ) );
				text.Style.SetMargin( Edge.All, StyleLength.Points( 1 ) );
				row.AddChild( text );
			}
		}

		return root;
	}

	//The shape of an inspector: a fixed-width column of groups, each a column of rows, each row a label and an
	//auto-sized value box with text inside. Deep enough to measure nested auto containers, wide enough to matter.
	private static IFixtureNode Inspector( IFixtureEngine engine, List<IFixtureNode> all, List<IFixtureNode> texts, out float width, out float height )
	{
		width = 420;
		height = float.NaN;
		var rng = new Rng( 23 );
		//The width comes from the owner (the dock the inspector sits in), so a resize reaches every row.
		var root = engine.CreateUiNode();
		root.Style.FlexDirection = FlexDirection.Column;
		all.Add( root );

		for ( var g = 0; g < 25; g++ )
		{
			var group = engine.CreateUiNode();
			group.Style.FlexDirection = FlexDirection.Column;
			group.Style.SetPadding( Edge.All, StyleLength.Points( 4 ) );
			group.Style.SetMargin( Edge.Bottom, StyleLength.Points( 6 ) );
			all.Add( group );
			root.AddChild( group );

			var header = engine.CreateUiNode();
			header.Style.FlexDirection = FlexDirection.Row;
			header.Style.SetPadding( Edge.Vertical, StyleLength.Points( 3 ) );
			all.Add( header );
			group.AddChild( header );
			header.AddChild( Text( engine, all, texts, rng, rng.Between( 1, 3 ) ) );

			for ( var r = 0; r < 20; r++ )
			{
				var row = engine.CreateUiNode();
				row.Style.FlexDirection = FlexDirection.Row;
				row.Style.AlignItems = Align.Center;
				row.Style.SetPadding( Edge.Vertical, StyleLength.Points( 2 ) );
				all.Add( row );
				group.AddChild( row );

				var label = Text( engine, all, texts, rng, rng.Between( 1, 2 ) );
				label.Style.Width = StyleLength.Percent( 40 );
				row.AddChild( label );

				var value = engine.CreateUiNode();
				value.Style.FlexDirection = FlexDirection.Row;
				value.Style.FlexGrow = 1;
				value.Style.FlexShrink = 1;
				value.Style.SetPadding( Edge.All, StyleLength.Points( 2 ) );
				value.Style.SetBorder( Edge.All, StyleLength.Points( 1 ) );
				all.Add( value );
				row.AddChild( value );

				var inner = engine.CreateUiNode();
				inner.Style.FlexDirection = FlexDirection.Row;
				inner.Style.FlexGrow = 1;
				inner.Style.SetPadding( Edge.Horizontal, StyleLength.Points( 3 ) );
				all.Add( inner );
				value.AddChild( inner );
				inner.AddChild( Text( engine, all, texts, rng, rng.Between( 1, 4 ) ) );
			}
		}

		return root;
	}
}
