using System;
using System.Collections.Generic;
using System.Linq;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//The cache's reuse rules checked against the algorithm itself: with the verifier on, every container answer taken from the cache is recomputed
//and compared. A rule that hands out an answer the algorithm would not give shows up here as a mismatch, whatever the final rects say.
[TestClass]
public class CacheVerificationTests
{
	private static void Verified( Action layout, int seeds = 0 )
	{
		FlexLayout.VerifyMismatches = new List<string>();
		FlexLayout.VerifyCache = true;

		try
		{
			layout();
		}
		finally
		{
			FlexLayout.VerifyCache = false;
			FlexLayout.VerifyNesting = 1;
		}

		var mismatches = FlexLayout.VerifyMismatches;
		FlexLayout.VerifyMismatches = null;

		if ( mismatches.Count == 0 ) return;

		//The same allowance as the differential test's, for the same reason: a baseline read
		//off an earlier layout can make an exact answer differ from a recomputation made later.
		if ( seeds > 0 )
		{
			var baselineSeeds = mismatches
				.Select( m => ulong.Parse( m.Substring( 6, m.IndexOf( ']' ) - 6 ) ) )
				.Distinct()
				.Where( DifferentialTests.UsesBaseline )
				.Count();

			var otherSeeds = mismatches
				.Select( m => ulong.Parse( m.Substring( 6, m.IndexOf( ']' ) - 6 ) ) )
				.Distinct()
				.Count() - baselineSeeds;

			Console.WriteLine( $"verifier: {mismatches.Count} mismatches in {baselineSeeds + otherSeeds} seeds, {baselineSeeds} of them with baseline alignment" );

			if ( otherSeeds == 0 && baselineSeeds <= DifferentialTests.BaselineAllowance( seeds ) ) return;
		}

		Assert.Fail( $"{mismatches.Count} cached answer(s) differ from a recomputation; the first: {mismatches[0]}" );
	}

	[DataTestMethod]
	[DataRow( "plain" )]
	[DataRow( "percent" )]
	[DataRow( "minmax" )]
	[DataRow( "automargin" )]
	[DataRow( "wrap" )]
	[DataRow( "scroll" )]
	[DataRow( "mixed" )]
	[DataRow( "mixed_unbounded" )]
	[DataRow( "mixed_rtl" )]
	[DataRow( "mixed_fraction" )]
	[DataRow( "grow" )]
	[DataRow( "block" )]
	[DataRow( "grid" )]
	public void DeepTree( string scenario )
	{
		FlexLayout.VerifyContext = scenario;
		Verified( () => DeepTrees.DumpIncremental( FixtureEngines.Production, scenario ) );
	}

	[TestMethod]
	public void RandomTrees()
	{
		var seeds = DifferentialTests.SeedCount;

		//The random trees are shallow enough to verify the hits inside a recomputation as well, which puts the innermost wrong answer first in the list.
		FlexLayout.VerifyNesting = int.TryParse( Environment.GetEnvironmentVariable( "RINTEN_LAYOUT_VERIFY_NESTING" ), out var nesting ) && nesting > 0 ? nesting : 3;

		Verified( () =>
		{
			for ( var seed = 0; seed < seeds; seed++ )
			{
				var nodes = new List<IFixtureNode>();
				var texts = new List<IFixtureNode>();
				FlexLayout.VerifyContext = $"seed {seed}";
				var root = Core.RandomTrees.Build( FixtureEngines.Production, (ulong)seed, nodes, texts );

				root.CalculateLayout( 800, 600 );

				if ( texts.Count > 0 )
				{
					var index = seed % texts.Count;
					texts[index].Context = (string)texts[index].Context + " changed";
					texts[index].MarkDirty();
					root.CalculateLayout( 800, 600 );
				}

				root.Style.Width = StyleLength.Points( 500 );
				root.CalculateLayout( 500, float.NaN );
			}
		}, seeds );
	}
}
