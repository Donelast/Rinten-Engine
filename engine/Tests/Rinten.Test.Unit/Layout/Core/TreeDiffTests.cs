using System;
using System.Collections.Generic;
using System.Text;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//Debug aid: RINTEN_LAYOUT_DIFFTREE diffs a scenario against the reference; check innermost mismatch
[TestClass]
public class TreeDiffTests
{
	[TestMethod]
	public void DiffScenario()
	{
		var scenario = Environment.GetEnvironmentVariable( "RINTEN_LAYOUT_DIFFTREE" );
		if ( string.IsNullOrEmpty( scenario ) ) Assert.Inconclusive( "Set RINTEN_LAYOUT_DIFFTREE=<deep-tree scenario> to diff it." );

		var production = new List<IFixtureNode>();
		var reference = new List<IFixtureNode>();
		IFixtureNode p, r;

		//A number names a random-tree seed instead of a deep-tree scenario; "seed:pass" picks the pass
		//(0 initial, 1 after the text change, 2 after the resize), as the differential test runs them.
		if ( ulong.TryParse( scenario.Split( ':' )[0], out var seed ) )
		{
			var pass = scenario.Contains( ':' ) ? int.Parse( scenario.Split( ':' )[1] ) : 0;
			var productionTexts = new List<IFixtureNode>();
			var referenceTexts = new List<IFixtureNode>();
			p = RandomTrees.Build( FixtureEngines.Production, seed, production, productionTexts );
			r = RandomTrees.Build( FixtureEngines.Reference, seed, reference, referenceTexts );
			p.CalculateLayout( 800, 600 );
			r.CalculateLayout( 800, 600 );

			if ( pass >= 1 && productionTexts.Count > 0 )
			{
				var index = (int)( seed % (ulong)productionTexts.Count );
				productionTexts[index].Context = (string)productionTexts[index].Context + " changed";
				productionTexts[index].MarkDirty();
				referenceTexts[index].Context = (string)referenceTexts[index].Context + " changed";
				referenceTexts[index].MarkDirty();
				p.CalculateLayout( 800, 600 );
				r.CalculateLayout( 800, 600 );
			}

			if ( pass >= 2 )
			{
				p.Style.Width = StyleLength.Points( 500 );
				r.Style.Width = StyleLength.Points( 500 );
				p.CalculateLayout( 500, float.NaN );
				r.CalculateLayout( 500, float.NaN );
			}
		}
		else
		{
			p = DeepTrees.Build( FixtureEngines.Production, scenario, production, out _, out var w, out var h, out var direction );
			r = DeepTrees.Build( FixtureEngines.Reference, scenario, reference, out _, out _, out _, out _ );
			p.CalculateLayout( w, h, direction );
			r.CalculateLayout( w, h, direction );
		}

		var sb = new StringBuilder();
		var mismatches = 0;

		for ( var i = 0; i < production.Count; i++ )
		{
			var a = production[i];
			var b = reference[i];
			if ( Same( a.LayoutLeft, b.LayoutLeft ) && Same( a.LayoutTop, b.LayoutTop ) && Same( a.LayoutWidth, b.LayoutWidth ) && Same( a.LayoutHeight, b.LayoutHeight ) )
				continue;

			mismatches++;
			var childrenAgree = true;
			for ( var c = 0; c < a.ChildCount; c++ )
			{
				var ca = a.GetChild( c );
				var cb = b.GetChild( c );
				if ( !( Same( ca.LayoutLeft, cb.LayoutLeft ) && Same( ca.LayoutTop, cb.LayoutTop ) && Same( ca.LayoutWidth, cb.LayoutWidth ) && Same( ca.LayoutHeight, cb.LayoutHeight ) ) )
					childrenAgree = false;
			}

			sb.AppendLine( $"node {i} parent {production.IndexOf( a.Owner )} {(childrenAgree ? "INNERMOST " : "")}production {DifferentialTests.Rect( a )} reference {DifferentialTests.Rect( b )} children={a.ChildCount} text={a.HasMeasureFunc} ctx={a.Context}" );
			sb.AppendLine( "  " + DifferentialTests.DescribeStyle( a.Style ) );

			if ( childrenAgree )
			{
				for ( var c = 0; c < a.ChildCount; c++ )
				{
					var ca = a.GetChild( c );
					sb.AppendLine( $"    child {production.IndexOf( ca )} {DifferentialTests.Rect( ca )} text={ca.HasMeasureFunc} ctx={ca.Context}" );
					sb.AppendLine( "      " + DifferentialTests.DescribeStyle( ca.Style ) );
				}

				//And the way up, since an absolute box is placed by an ancestor.
				for ( var up = a.Owner; up is not null; up = up.Owner )
				{
					sb.AppendLine( $"    ancestor {production.IndexOf( up )} production {DifferentialTests.Rect( up )} reference {DifferentialTests.Rect( reference[production.IndexOf( up )] )}" );
					sb.AppendLine( "      " + DifferentialTests.DescribeStyle( up.Style ) );
				}
			}
		}

		sb.Insert( 0, $"{scenario}: {mismatches} of {production.Count} nodes differ\n" );
		Console.WriteLine( sb.ToString() );
		Assert.Inconclusive( sb.ToString() );
	}

	private static bool Same( float a, float b ) => a == b || ( float.IsNaN( a ) && float.IsNaN( b ) ) || MathF.Abs( a - b ) <= 0.01f;
}
