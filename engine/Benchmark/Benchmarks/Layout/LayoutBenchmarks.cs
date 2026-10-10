using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Rinten.Layout;

namespace Benchmarks.Layout;

//Layout passes on the LayoutTrees shapes: all dirty, one leaf dirty, resize, and tree build
[MemoryDiagnoser]
public class LayoutBenchmarks
{
	public static IConfig Config => ManualConfig
		.Create( DefaultConfig.Instance )
		.WithOptions( ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile )
		.AddJob( Job.Default.WithWarmupCount( 3 ).WithIterationCount( 12 ) );

	[Params( "deep_plain", "deep_grow", "widerow", "inspector", "grid" )]
	public string Shape { get; set; }

	private LayoutTrees.Tree tree;
	private bool narrow;

	[GlobalSetup]
	public void Setup()
	{
		tree = LayoutTrees.Build( Shape );
		leafText = (string)tree.Texts[tree.Texts.Count / 2].Context;
		leafTextChanged = leafText + " changed";
		FlexLayout.Calculate( tree.Root, tree.Width, tree.Height, Direction.LTR );
	}

	//Every node dirty, as after a stylesheet change. The dirtying walk is part of the cost.
	[Benchmark]
	public void AllDirty()
	{
		foreach ( var node in tree.All ) node.MarkDirtyAndPropagate();
		FlexLayout.Calculate( tree.Root, tree.Width, tree.Height, Direction.LTR );
	}

	//One deep text leaf changed; only the path to the root is dirty. The text alternates between
	//two values rather than growing, so a million invocations measure the same work as one.
	[Benchmark]
	public void LeafRelayout()
	{
		var leaf = tree.Texts[tree.Texts.Count / 2];
		leafToggle = !leafToggle;
		leaf.Context = leafToggle ? leafTextChanged : leafText;
		leaf.MarkDirtyAndPropagate();
		FlexLayout.Calculate( tree.Root, tree.Width, tree.Height, Direction.LTR );
	}

	private bool leafToggle;
	private string leafText;
	private string leafTextChanged;

	//Nothing dirty; the root's available width alternates so every cache entry is consulted.
	[Benchmark]
	public void Resize()
	{
		narrow = !narrow;
		FlexLayout.Calculate( tree.Root, narrow ? tree.Width - 137 : tree.Width, float.NaN, Direction.LTR );
	}

	//Building the tree and its first layout: the cost of opening a window.
	[Benchmark]
	public int BuildAndLayout()
	{
		var fresh = LayoutTrees.Build( Shape );
		FlexLayout.Calculate( fresh.Root, fresh.Width, fresh.Height, Direction.LTR );
		return fresh.All.Count;
	}
}
