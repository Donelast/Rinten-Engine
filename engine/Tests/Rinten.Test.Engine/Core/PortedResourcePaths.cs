using System;
using System.IO;
using Rinten.Porting;
using Rinten.Porting.SBox;

namespace EngineTests;

//Ported .vmdl paths resolve to the port's .mdl definition, not just the compiled geometry
[TestClass]
public class PortedResourcePathsTest
{
	private static string Temp()
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-paths-{Guid.NewGuid():N}" );

		Directory.CreateDirectory( Path.Combine( path, "Assets", "models" ) );

		return path;
	}

	[TestMethod]
	public void ThePrefabAsksForTheModelThePortWrote()
	{
		var project = Temp();
		var assets = Path.Combine( project, "Assets" );

		try
		{
			File.WriteAllText( Path.Combine( assets, "models", "citizen.mdl" ), "{}" );

			var prefab = Path.Combine( assets, "player.prefab" );

			File.WriteAllText( prefab,
				"{ \"Model\": \"models/citizen.vmdl\", \"Other\": \"models/citizen.vmdl\" }" );

			var report = new PortReport();

			Assert.AreEqual( 2, SBoxResourcePaths.Rewrite( assets, project, report ) );

			var written = File.ReadAllText( prefab );

			Assert.IsTrue( written.Contains( "models/citizen.mdl" ), written );
			Assert.IsFalse( written.Contains( ".vmdl" ), written );
		}
		finally
		{
			Directory.Delete( project, true );
		}
	}

	//A name with nothing of ours behind it is left exactly as it was. Base content the port did not touch keeps being found the
	//way it is found now. Rewriting a name the port did not write a file for would break something that currently works, for nothing.
	[TestMethod]
	public void AndLeavesTheOnesItDidNotWrite()
	{
		var project = Temp();
		var assets = Path.Combine( project, "Assets" );

		try
		{
			var prefab = Path.Combine( assets, "props.prefab" );

			File.WriteAllText( prefab, "{ \"Model\": \"models/dev/box.vmdl\" }" );

			var report = new PortReport();

			Assert.AreEqual( 0, SBoxResourcePaths.Rewrite( assets, project, report ) );
			Assert.IsTrue( File.ReadAllText( prefab ).Contains( "models/dev/box.vmdl" ) );
		}
		finally
		{
			Directory.Delete( project, true );
		}
	}

	//Code too. A project names models in its code as often as in its scenes, and a scene converter never gets near it.
	[TestMethod]
	public void TheCodeAsksForItToo()
	{
		var project = Temp();
		var assets = Path.Combine( project, "Assets" );

		try
		{
			File.WriteAllText( Path.Combine( assets, "models", "sword.mdl" ), "{}" );

			var code = Path.Combine( project, "Code" );
			Directory.CreateDirectory( code );

			var file = Path.Combine( code, "Weapon.cs" );

			File.WriteAllText( file, "var model = Model.Load( \"models/sword.vmdl\" );" );

			var report = new PortReport();

			Assert.AreEqual( 1, SBoxResourcePaths.Rewrite( assets, project, report ) );
			Assert.IsTrue( File.ReadAllText( file ).Contains( "models/sword.mdl" ) );
		}
		finally
		{
			Directory.Delete( project, true );
		}
	}
}
