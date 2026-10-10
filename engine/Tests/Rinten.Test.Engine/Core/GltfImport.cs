using System.IO;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Reading glTF. The geometry in these is deliberately lopsided - a triangle one metre along each of three different
//axes - because the two things that go wrong on import are axes and units, and neither shows up on a symmetric shape.
[TestClass]
public class GltfImportTest
{

	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-gltf-{System.Guid.NewGuid():N}{extension}" );

	//A glTF holding one triangle, with its buffer inline as base64 so the test is one file and no fixtures. Positions, in
	//metres and glTF's Y-up axes: (0,0,0) (1,0,0) (0,2,0) so x, y and z are all different and a swapped pair cannot pass.
	private static string WriteTriangle()
	{
		float[] positions = { 0, 0, 0, 1, 0, 0, 0, 2, 0 };
		ushort[] indices = { 0, 1, 2 };

		var bytes = new byte[( positions.Length * 4 ) + ( indices.Length * 2 )];
		System.Buffer.BlockCopy( positions, 0, bytes, 0, positions.Length * 4 );
		System.Buffer.BlockCopy( indices, 0, bytes, positions.Length * 4, indices.Length * 2 );

		var buffer = System.Convert.ToBase64String( bytes );

		var json = $$"""
		{
			"asset": { "version": "2.0" },
			"scene": 0,
			"scenes": [ { "nodes": [ 0 ] } ],
			"nodes": [ { "mesh": 0, "name": "tri" } ],
			"meshes": [ {
				"name": "triangle",
				"primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1, "material": 0 } ]
			} ],
			"materials": [ { "name": "stone" } ],
			"buffers": [ { "byteLength": {{bytes.Length}}, "uri": "data:application/octet-stream;base64,{{buffer}}" } ],
			"bufferViews": [
				{ "buffer": 0, "byteOffset": 0, "byteLength": {{positions.Length * 4}} },
				{ "buffer": 0, "byteOffset": {{positions.Length * 4}}, "byteLength": {{indices.Length * 2}} }
			],
			"accessors": [
				{
					"bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3",
					"min": [ 0, 0, 0 ], "max": [ 1, 2, 0 ]
				},
				{ "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
			]
		}
		""";

		var path = TempPath( ".gltf" );
		File.WriteAllText( path, json );
		return path;
	}

	//The trip in: a glTF, through the importer, into a compiled model, and back out with its axes and units changed and nothing else.
	[TestMethod]
	public unsafe void ImportsGeometryInOurAxesAndUnits()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var gltf = WriteTriangle();
		var compiled = TempPath( ".mdl_c" );

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			builder.SetName( "models/test/triangle.mdl" );

			var meshes = CoreGltfImport.ImportModel( builder, gltf, 1.0f );
			Assert.AreEqual( 1, meshes, "one primitive, one mesh" );

			Assert.AreEqual( 1, builder.Save( compiled ) );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( compiled );
		Assert.IsNotNull( model, "the compiled model should load" );

		try
		{
			Assert.AreEqual( 3, model.VertexCount );
			Assert.AreEqual( 3, model.IndexCount );

			var vertices = new Vertex[3];
			fixed ( Vertex* v = vertices )
			{
				model.CopyVertices( v, vertices.Length );
			}

			//glTF's axes are ours and both measure in metres, so nothing moves. The importer's whole job here
			//is to leave the numbers alone. (0,0,0) -> (0, 0, 0) (1,0,0) -> (1, 0, 0) (0,2,0) -> (0, 2, 0)
			Assert.AreEqual( Vector3.Zero, vertices[0].Position, "the origin stays put" );

			Assert.AreEqual( 1.0f, vertices[1].Position.x, 0.001f, "a metre along glTF x is a metre along x here" );
			Assert.AreEqual( 0.0f, vertices[1].Position.y, 0.001f, "and nowhere else" );
			Assert.AreEqual( 0.0f, vertices[1].Position.z, 0.001f );

			Assert.AreEqual( 0.0f, vertices[2].Position.x, 0.001f, "two metres up in glTF" );
			Assert.AreEqual( 2.0f, vertices[2].Position.y, 0.001f, "is two metres up here" );
			Assert.AreEqual( 0.0f, vertices[2].Position.z, 0.001f );

			Assert.AreEqual( 1, model.MeshCount );
			Assert.AreEqual( "triangle", model.MeshName( 0 ) );
			Assert.AreEqual( "stone", model.MeshMaterial( 0 ) );

			//Bounds were not given, so they are measured from the geometry - which only comes out right if the conversion above happened before they were.
			var bounds = stackalloc float[6];
			model.GetRenderBounds( bounds );

			Assert.AreEqual( 1.0f, bounds[3], 0.001f, "furthest x" );
			Assert.AreEqual( 2.0f, bounds[4], 0.001f, "highest y" );
		}
		finally
		{
			model.Dispose();
			File.Delete( gltf );
			File.Delete( compiled );
		}
	}

	//The scale on the definition multiplies what the file says, for exports that disagree with the spec about what a unit is.
	[TestMethod]
	public unsafe void ScaleMultipliesWhatTheFileSays()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var gltf = WriteTriangle();
		var compiled = TempPath( ".mdl_c" );
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			CoreGltfImport.ImportModel( builder, gltf, 2.0f );
			builder.Save( compiled );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( compiled );

		try
		{
			var vertices = new Vertex[3];
			fixed ( Vertex* v = vertices )
			{
				model.CopyVertices( v, vertices.Length );
			}

			Assert.AreEqual( 2.0f, vertices[1].Position.x, 0.001f, "twice as far" );
		}
		finally
		{
			model.Dispose();
			File.Delete( gltf );
			File.Delete( compiled );
		}
	}

	//Collision built from the imported mesh, so a model can be walked into without anyone drawing hulls by hand.
	[TestMethod]
	public void BuildsCollisionFromTheMesh()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var gltf = WriteTriangle();
		var compiled = TempPath( ".mdl_c" );
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			CoreGltfImport.ImportModel( builder, gltf, 1.0f );

			Assert.AreEqual( 1, builder.CollisionFromMesh( 1, "surfaces/stone.surface", 0.0f ),
				"the triangles should have become collision" );

			builder.Save( compiled );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( compiled );

		try
		{
			Assert.AreEqual( 1, model.BodyCount, "one collision body" );
			Assert.AreEqual( 1, model.ShapeCount( 0 ), "with one shape" );

			Assert.AreEqual( (int)ModelFormat.ShapeKind.Mesh, model.ShapeKind( 0, 0 ) );
			Assert.AreEqual( 3, model.ShapePointCount( 0, 0 ), "the triangle's corners" );
			Assert.AreEqual( 3, model.ShapeIndexCount( 0, 0 ) );

			Assert.AreEqual( "surfaces/stone.surface",
				model.ShapeSurface( 0, 0 ) );
		}
		finally
		{
			model.Dispose();
			File.Delete( gltf );
			File.Delete( compiled );
		}
	}
}
