using System;
using System.IO;
using System.Text.Json.Nodes;
using Rinten.Porting;

namespace EngineTests;

//Runs the converter on a real legacy scene; the report must name any component it can't cover
[TestClass]
public class SceneUnitsRealSceneTest
{
	//Where the fixture lives. Skipped rather than failed when it is absent, because a
	//scene is somebody's content and this is checked into a repo that may not carry it.
	private static string Fixture =>
		Path.Combine( System.AppContext.BaseDirectory, "Fixtures", "sbox-minimal.scene" );

	[TestMethod]
	public void ARealSBoxSceneConvertsWithNothingLeftUnnamed()
	{
		var path = Path.GetFullPath( Fixture );

		if ( !File.Exists( path ) )
		{
			Assert.Inconclusive( $"no fixture at {path}" );
			return;
		}

		var scene = JsonNode.Parse( File.ReadAllText( path ) );
		var report = new PortReport();

		var changed = SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "sbox-minimal.scene", report );

		Assert.IsTrue( changed > 0, "a scene of inches should have had something to convert" );

		//The engine's own components are the engine's own problem, and every one of them has to be named. A game's components are not: nobody
		//here knows what MoveSpeed on somebody's controller measures, and the port reporting it is the correct outcome rather than a gap.
		var ours = report.Unconverted
			.Where( u => u.What.StartsWith( "Rinten.", StringComparison.Ordinal ) )
			.Select( u => $"{u.What} = {u.Value}" )
			.Distinct()
			.ToList();

		if ( ours.Count > 0 )
		{
			Assert.Fail(
				$"the dimension table does not cover this scene. Add an entry per line - " +
				$"Length, Speed or None - to SceneUnits.Dimensions:\n  {string.Join( "\n  ", ours )}" );
		}

		Assert.IsTrue( report.Unconverted.Any( u => !u.What.StartsWith( "Rinten.", StringComparison.Ordinal ) ),
			"the game's own component properties should still be reported, so a person sees them" );
	}
}
