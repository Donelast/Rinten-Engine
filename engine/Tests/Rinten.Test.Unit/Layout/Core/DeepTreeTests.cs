using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LayoutTests.Harness;

namespace LayoutTests.Core;

//Incremental layout matches fresh within 0.001, and rects match the Data/Layout/deeptree snapshots
[TestClass]
public class DeepTreeTests
{
	[TestMethod] public void Plain() => Check( "plain" );
	[TestMethod] public void Percent() => Check( "percent" );
	[TestMethod] public void MinMax() => Check( "minmax" );
	[TestMethod] public void AutoMargin() => Check( "automargin" );
	[TestMethod] public void Wrap() => Check( "wrap" );
	[TestMethod] public void Scroll() => Check( "scroll" );
	[TestMethod] public void Mixed() => Check( "mixed" );
	[TestMethod] public void MixedUnbounded() => Check( "mixed_unbounded" );
	[TestMethod] public void MixedRtl() => Check( "mixed_rtl" );
	[TestMethod] public void MixedFraction() => Check( "mixed_fraction" );
	[TestMethod] public void Grow() => Check( "grow" );
	[TestMethod] public void Block() => Check( "block" );
	[TestMethod] public void Grid() => Check( "grid" );

	public const string WriteVariable = "RINTEN_LAYOUT_WRITE_SNAPSHOTS";

	//Constraints within 0.0001 are treated as equal. Anything beyond this tolerance is a real difference.
	private const float Tolerance = 0.001f;

	private static string SnapshotPath( string scenario ) => Path.Combine( KnownFailures.DataDirectory, "deeptree", scenario + ".txt" );

	//Scenarios where the cache is inexact; phase 4 keys it on the resolve reference and empties this
	private static readonly HashSet<string> InexactCacheScenarios = new()
	{
		//Empty since phase 4b: every scenario lays out the same whether fresh or incrementally.
	};

	private static void Check( string scenario )
	{
		var engine = FixtureEngines.Production;
		var actual = DeepTrees.DumpIncremental( engine, scenario );

		var writeTo = Environment.GetEnvironmentVariable( WriteVariable );
		if ( !string.IsNullOrEmpty( writeTo ) )
		{
			Directory.CreateDirectory( writeTo );
			File.WriteAllText( Path.Combine( writeTo, scenario + ".txt" ), actual );
		}
		else
		{
			var path = SnapshotPath( scenario );
			Assert.IsTrue( File.Exists( path ), $"No snapshot for '{scenario}' at {path}. Generate one with {WriteVariable}." );
			CompareDumps( scenario + " (snapshot)", File.ReadAllText( path ), actual );
		}

		var fresh = DeepTrees.DumpFresh( engine, scenario );
		if ( InexactCacheScenarios.Contains( scenario ) )
		{
			//Still exercised, so the day the cache becomes exact this says so and the set shrinks.
			Assert.IsFalse( DumpsAgree( fresh, actual ), $"{scenario}: incremental layout now matches a fresh tree; remove it from InexactCacheScenarios." );
			return;
		}

		CompareDumps( scenario + " (incremental vs fresh)", fresh, actual );
	}

	private static bool DumpsAgree( string expected, string actual )
	{
		try
		{
			CompareDumps( "probe", expected, actual );
			return true;
		}
		catch ( AssertFailedException )
		{
			return false;
		}
	}

	internal static void CompareDumps( string scenario, string expected, string actual, float tolerance = Tolerance )
	{
		if ( actual == expected ) return;

		var expectedPasses = expected.Split( '\n' );
		var actualPasses = actual.Split( '\n' );
		Assert.AreEqual( expectedPasses.Length, actualPasses.Length, $"{scenario}: pass count" );

		for ( var p = 0; p < expectedPasses.Length; p++ )
		{
			var e = expectedPasses[p].Split( ';' );
			var a = actualPasses[p].Split( ';' );
			Assert.AreEqual( e.Length, a.Length, $"{scenario}: pass {p} node count" );

			for ( var i = 0; i < e.Length; i++ )
			{
				if ( e[i] == a[i] ) continue;

				var ev = e[i].Split( ',' );
				var av = a[i].Split( ',' );
				for ( var k = 0; k < 4; k++ )
				{
					var expectedValue = float.Parse( ev[k], CultureInfo.InvariantCulture );
					var actualValue = float.Parse( av[k], CultureInfo.InvariantCulture );
					if ( float.IsNaN( expectedValue ) && float.IsNaN( actualValue ) ) continue;
					if ( float.IsNaN( expectedValue ) || float.IsNaN( actualValue ) || MathF.Abs( expectedValue - actualValue ) > tolerance )
					{
						Assert.Fail( $"{scenario}: pass {p}, node {i}: expected [{e[i]}] got [{a[i]}]" );
					}
				}
			}
		}
	}
}
