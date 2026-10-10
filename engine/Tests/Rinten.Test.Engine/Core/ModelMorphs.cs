using System;
using Rinten;

namespace EngineTests;

//Blend shapes built on a mesh are reported by name on the compiled model
[TestClass]
[DoNotParallelize]
public class ModelMorphTests
{
	private static Mesh Triangle()
	{
		var mesh = new Mesh();

		mesh.CreateVertexBuffer( 3, new[]
		{
			new Vertex { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new Vertex { Position = new Vector3( 1, 0, 0 ), Normal = Vector3.Up },
			new Vertex { Position = new Vector3( 1, 1, 0 ), Normal = Vector3.Up },
		}.AsSpan() );

		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		return mesh;
	}

	//A shape authored on a mesh is a shape the model has, by name.
	[TestMethod]
	public void AModelKeepsTheShapesItsMeshesCarry()
	{
		ThreadSafe.MarkMainThread();

		var mesh = Triangle();

		mesh.AddMorph( "smile", new[]
		{
			new MorphDelta( 0, new Vector3( 0, 0, 1 ), Vector3.Zero ),
			new MorphDelta( 2, new Vector3( 0, 0, 2 ), Vector3.Zero ),
		} );

		var model = new ModelBuilder().AddMesh( mesh ).Create();

		Assert.IsTrue( model.IsValid(), "the model should have been built" );
		Assert.AreEqual( 1, model.MorphCount, "the model should carry the one shape" );
		Assert.AreEqual( "smile", model.GetMorphName( 0 ) );
		Assert.AreEqual( 0, model.Morphs.GetIndex( "smile" ) );
	}

	//Two shapes are two, and a name nobody authored is not one of them.
	[TestMethod]
	public void AShapeTheModelHasNotGotIsNotIndexNought()
	{
		ThreadSafe.MarkMainThread();

		var mesh = Triangle();

		mesh.AddMorph( "smile", new[] { new MorphDelta( 0, new Vector3( 0, 0, 1 ), Vector3.Zero ) } );
		mesh.AddMorph( "frown", new[] { new MorphDelta( 1, new Vector3( 0, 0, -1 ), Vector3.Zero ) } );

		var model = new ModelBuilder().AddMesh( mesh ).Create();

		Assert.AreEqual( 2, model.MorphCount );

		//The trap this replaced: GetIndex answered nought for a name it did not have, which is a
		//real morph, so anything setting a shape by a mistyped name moved the first one instead.
		Assert.AreEqual( -1, model.Morphs.GetIndex( "grimace" ) );

		Assert.AreEqual( 0, model.Morphs.GetIndex( "smile" ) );
		Assert.AreEqual( 1, model.Morphs.GetIndex( "frown" ) );
	}

	//A model nobody gave a shape to has none, and says so.
	[TestMethod]
	public void AModelWithoutShapesCarriesNone()
	{
		ThreadSafe.MarkMainThread();

		var model = new ModelBuilder().AddMesh( Triangle() ).Create();

		Assert.IsTrue( model.IsValid() );
		Assert.AreEqual( 0, model.MorphCount );
		Assert.AreEqual( -1, model.Morphs.GetIndex( "smile" ) );
	}
}
