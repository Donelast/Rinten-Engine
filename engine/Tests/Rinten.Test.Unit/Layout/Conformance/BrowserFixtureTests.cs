using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LayoutTests.Harness;
using LayoutTests.Harness.Fixtures;
using Rinten.Layout;

namespace LayoutTests.Conformance;

//Every browser fixture against the production engine. A case is built from its CSS on both sides; the browser's rects are the expectation,
//the engine's rects the result, compared at a tenth of a pixel. A case that disagrees is signed in the known-failure list, never edited.
[TestClass]
public class BrowserFixtureTests
{
	private const float Tolerance = 0.1f;

	private static readonly Lazy<List<FixtureCase>> cases = new( () => FixtureFiles.ReadCases( Path.Combine( KnownFailures.DataDirectory, FixtureFiles.CasesFile ) ) );
	private static readonly Lazy<Dictionary<string, float[][]>> expected = new( () => FixtureFiles.ReadExpected( Path.Combine( KnownFailures.DataDirectory, FixtureFiles.ExpectedFile ) ) );
	private static readonly Lazy<Dictionary<string, FixtureCase>> byName = new( () => cases.Value.ToDictionary( c => c.Name, StringComparer.Ordinal ) );

	public static IEnumerable<object[]> CaseNames()
	{
		foreach ( var c in cases.Value ) yield return new object[] { c.Name };
	}

	[DataTestMethod]
	[DynamicData( nameof( CaseNames ), DynamicDataSourceType.Method )]
	public void Case( string name )
	{
		var verdict = KnownFailures.Judge( KnownFailures.Browser, name, Run( FixtureEngines.Production, byName.Value[name], expected.Value ) );
		if ( verdict is not null ) Assert.Fail( verdict );
	}

	//Lays the case out and returns the mismatch report, empty when it agrees with the browser.
	internal static List<string> Run( IFixtureEngine engine, FixtureCase fixture, Dictionary<string, float[][]> expectations )
	{
		if ( !expectations.TryGetValue( fixture.Name, out var rects ) )
			return new List<string> { "no browser expectation recorded; regenerate browser-expected.json" };

		var nodes = new List<(IFixtureNode Node, string Path)>();
		var root = Materialize( engine, fixture.Root, nodes );
		var direction = fixture.Root.Css.TryGetValue( "direction", out var dir ) && dir == "rtl" ? Direction.RTL : Direction.LTR;

		//The root behaves like a block-level box in the owner: it is the containing block for its absolute descendants, it fills the owner's
		//width, and its height comes from its content unless it says otherwise. The owner height is only a reference when the root names one.
		if ( !fixture.Root.Css.ContainsKey( "position" ) ) root.Style.PositionType = PositionType.Relative;
		var ownerHeight = fixture.Root.Css.ContainsKey( "height" ) ? FixtureCase.OwnerHeight : float.NaN;
		root.CalculateLayout( FixtureCase.OwnerWidth, ownerHeight, direction );

		var report = new List<string>();
		if ( rects.Length != nodes.Count )
		{
			report.Add( $"node count: browser {rects.Length}, engine {nodes.Count}" );
			return report;
		}

		var ci = CultureInfo.InvariantCulture;
		for ( var i = 0; i < nodes.Count; i++ )
		{
			var (node, path) = nodes[i];
			var actual = new[] { node.LayoutLeft, node.LayoutTop, node.LayoutWidth, node.LayoutHeight };
			var same = true;
			for ( var k = 0; k < 4; k++ ) same &= MathF.Abs( actual[k] - rects[i][k] ) <= Tolerance;
			if ( same ) continue;

			report.Add( $"{path}: browser [{Join( rects[i], ci )}] engine [{Join( actual, ci )}]" );
		}

		return report;
	}

	private static string Join( float[] rect, CultureInfo ci ) => string.Join( ", ", rect.Select( v => v.ToString( "0.###", ci ) ) );

	private static IFixtureNode Materialize( IFixtureEngine engine, CaseNode description, List<(IFixtureNode, string)> nodes, string path = "root" )
	{
		var node = engine.CreateUiNode();
		CssStyleMapper.Apply( node.Style, description.Css );
		nodes.Add( (node, path) );
		for ( var i = 0; i < description.Children.Count; i++ )
			node.AddChild( Materialize( engine, description.Children[i], nodes, $"{path}/{i}" ) );
		return node;
	}
}

//The committed case list is the catalogue's output, and every case has a browser
//expectation. Set FixtureFiles.WriteVariable to a directory to write the list and the page.
[TestClass]
public class FixtureCatalogueTests
{
	[TestMethod]
	public void CommittedCasesMatchTheCatalogue()
	{
		var generated = FixtureFiles.Serialize( CaseCatalogue.All() );

		var writeTo = Environment.GetEnvironmentVariable( FixtureFiles.WriteVariable );
		if ( !string.IsNullOrEmpty( writeTo ) )
		{
			Directory.CreateDirectory( writeTo );
			File.WriteAllText( Path.Combine( writeTo, FixtureFiles.CasesFile ), generated );
			File.WriteAllText( Path.Combine( writeTo, FixtureFiles.PageFile ), FixtureFiles.RenderPage( CaseCatalogue.All() ) );
			Console.WriteLine( $"wrote {CaseCatalogue.All().Count} cases to {writeTo}" );
			return;
		}

		var path = Path.Combine( KnownFailures.DataDirectory, FixtureFiles.CasesFile );
		Assert.IsTrue( File.Exists( path ), $"No case list at {path}; generate one with {FixtureFiles.WriteVariable}." );
		Assert.AreEqual( File.ReadAllText( path ), generated, "The catalogue changed. Regenerate the case list and the browser expectations together." );
	}

	[TestMethod]
	public void EveryCaseHasABrowserExpectation()
	{
		var expected = FixtureFiles.ReadExpected( Path.Combine( KnownFailures.DataDirectory, FixtureFiles.ExpectedFile ) );
		var missing = CaseCatalogue.All().Where( c => !expected.ContainsKey( c.Name ) ).Select( c => c.Name ).ToList();
		Assert.AreEqual( 0, missing.Count, "Cases without a browser expectation: " + string.Join( ", ", missing.Take( 20 ) ) );

		var stale = expected.Keys.Except( CaseCatalogue.All().Select( c => c.Name ) ).ToList();
		Assert.AreEqual( 0, stale.Count, "Expectations for cases that no longer exist: " + string.Join( ", ", stale.Take( 20 ) ) );
	}

	[TestMethod]
	public void ReferenceEngineFailsOnlySignedCases()
	{
		var expected = FixtureFiles.ReadExpected( Path.Combine( KnownFailures.DataDirectory, FixtureFiles.ExpectedFile ) );
		var problems = new List<string>();
		var failing = 0;
		foreach ( var c in CaseCatalogue.All() )
		{
			var report = BrowserFixtureTests.Run( FixtureEngines.Reference, c, expected );
			if ( report.Count > 0 ) failing++;
			var verdict = KnownFailures.Judge( KnownFailures.Browser, c.Name, report );
			if ( verdict is not null ) problems.Add( verdict.Split( '\n' )[0] );
		}

		Console.WriteLine( $"reference: {failing} failing of {CaseCatalogue.All().Count}, {KnownFailures.Browser.Count} signed" );
		Assert.AreEqual( 0, problems.Count, "The reference engine disagrees with the signed list:\n" + string.Join( "\n", problems ) );
	}
}
