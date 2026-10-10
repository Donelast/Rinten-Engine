using System;
using System.Collections.Generic;
using LayoutTests.Harness;

namespace LayoutTests.Core;

//A warm layout pass allocates nothing; the measure function scans text to avoid its own allocs
[TestClass]
public class AllocationTests
{
	[TestMethod]
	[DataRow( "deeptree:plain" )]
	[DataRow( "deeptree:mixed" )]
	[DataRow( "widerow" )]
	[DataRow( "inspector" )]
	public void WarmPassesAllocateNothing( string shape )
	{
		var all = new List<IFixtureNode>();
		var texts = new List<IFixtureNode>();
		var root = TreeShapes.Build( FixtureEngines.Production, shape, all, texts, out var width, out var height );
		root.CalculateLayout( width, height );

		var leaf = texts[texts.Count / 2];
		var text = (string)leaf.Context;
		var changed = text + " changed";

		//A few passes first so every pool and every cache slot has been touched once.
		for ( var pass = 0; pass < 3; pass++ )
		{
			foreach ( var node in all ) node.MarkDirty();
			root.CalculateLayout( width, height );
			leaf.Context = pass % 2 == 0 ? changed : text;
			leaf.MarkDirty();
			root.CalculateLayout( width, height );
			root.CalculateLayout( width - 137, float.NaN );
			root.CalculateLayout( width, height );
		}

		foreach ( var node in all ) node.MarkDirty();
		var before = GC.GetAllocatedBytesForCurrentThread();
		root.CalculateLayout( width, height );
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before, $"{shape}: an all-dirty pass allocated" );

		leaf.Context = text;
		leaf.MarkDirty();
		before = GC.GetAllocatedBytesForCurrentThread();
		root.CalculateLayout( width, height );
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before, $"{shape}: a one-leaf relayout allocated" );

		before = GC.GetAllocatedBytesForCurrentThread();
		root.CalculateLayout( width - 137, float.NaN );
		Assert.AreEqual( 0L, GC.GetAllocatedBytesForCurrentThread() - before, $"{shape}: a resize allocated" );
	}
}
