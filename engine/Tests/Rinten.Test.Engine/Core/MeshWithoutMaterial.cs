using System;
using System.IO;
using Rinten;
using Rinten.Engine;

namespace EngineTests;

//Editable mesh faces with no material still build a model instead of throwing in Rebuild
[TestClass]
[DoNotParallelize]
public class MeshWithoutMaterialTests
{
	private static PolygonMesh Quad( out HalfEdgeMesh.FaceHandle face )
	{
		var mesh = new PolygonMesh();

		var a = mesh.AddVertex( new Vector3( 0, 0, 0 ) );
		var b = mesh.AddVertex( new Vector3( 1, 0, 0 ) );
		var c = mesh.AddVertex( new Vector3( 1, 1, 0 ) );
		var d = mesh.AddVertex( new Vector3( 0, 1, 0 ) );

		face = mesh.AddFace( a, b, c, d );

		return mesh;
	}

	//A face with no material still builds a model, and the model is geometry.
	[TestMethod]
	public void AFaceWithNoMaterialStillBuilds()
	{
		ThreadSafe.MarkMainThread();

		var mesh = Quad( out var face );

		Assert.IsTrue( face.IsValid );

		//What the block tool does with its active material when that material is one the project does not have: hands over nothing.
		mesh.SetFaceMaterial( face, (Material)null );

		var model = mesh.Rebuild();

		Assert.IsNotNull( model, "a face without a material is still a face" );
		Assert.AreEqual( 1, model.MeshCount );
		Assert.IsNull( model.pulled, "and it is geometry, not bytes for somebody's shader" );
	}

	//The fallback dev material named by the mesh and its tool actually ships with the engine
	[TestMethod]
	public void TheDevMaterialIsOneTheEngineHas()
	{
		var game = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsFalse( string.IsNullOrWhiteSpace( game ), "the tests need the engine path" );

		var grid = Path.Combine( game, "addons", "base", "Assets", "materials", "dev", "grid.mat" );

		Assert.IsTrue( File.Exists( grid ), $"{grid} is not there" );

		//And what it draws with, since a material naming a picture that is not there is a material that resolves to the checkerboard.
		var picture = Path.Combine( game, "addons", "base", "Assets", "materials", "dev", "grid.png" );

		Assert.IsTrue( File.Exists( picture ), $"{picture} is not there" );
	}
}
