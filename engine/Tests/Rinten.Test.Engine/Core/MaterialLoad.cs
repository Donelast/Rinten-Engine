using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;

namespace EngineTests.Core;

//Material.Load reads our own .mat files, not just .vmat
[TestClass]
[DoNotParallelize]
public class MaterialLoadTests
{
	private static string Write( string name, string json )
	{
		//Loading asserts it is on the main thread, and a test is not one until it says so.
		ThreadSafe.MarkMainThread();

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-material-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var path = Path.Combine( directory, name );
		File.WriteAllText( path, json );

		return path;
	}

	private const string Definition = """
		{
		  "Shader": "materials/voxel.shader",
		  "Tint": "0.5,0.25,0.125,1",
		  "ColorMap": "",
		  "Normal": "",
		  "RoughMetalAmbient": "",
		  "Emission": "",
		  "Roughness": 0.75,
		  "Metalness": 0.25,
		  "EmissionStrength": 0,
		  "PhysicsSurface": "",
		  "TwoSided": false,
		  "Blend": 0,
		  "AlphaCutoff": 0.5,
		  "__references": null
		}
		""";

	[TestMethod]
	public void OurOwnMaterialFileLoadsByName()
	{
		var path = Write( "surface.mat", Definition );

		try
		{
			var material = Material.Load( path );

			Assert.IsNotNull( material, "a .mat is a material this engine can read" );
		}
		finally
		{
			Directory.Delete( Path.GetDirectoryName( path ), true );
		}
	}

	//Asking for a .vmat name finds the converted .mat beside it
	[TestMethod]
	public void AMaterialAskedForByTheirNameFindsOurs()
	{
		var path = Write( "voxel.mat", Definition );

		try
		{
			var theirs = Path.ChangeExtension( path, ".vmat" );

			Assert.IsFalse( File.Exists( theirs ), "the porter leaves none of theirs behind" );

			Assert.IsNotNull( Material.Load( theirs ),
				"the name means the file beside it, as it does for models and sounds" );
		}
		finally
		{
			Directory.Delete( Path.GetDirectoryName( path ), true );
		}
	}

	//A name with nothing under it is still nothing, rather than something half-read.
	[TestMethod]
	public void AMaterialThatIsNotThereIsNull()
	{
		ThreadSafe.MarkMainThread();

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-material-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		try
		{
			Assert.IsNull( Material.Load( Path.Combine( directory, "nothing.mat" ) ) );
			Assert.IsNull( Material.Load( Path.Combine( directory, "nothing.vmat" ) ) );
		}
		finally
		{
			Directory.Delete( directory, true );
		}
	}
}
