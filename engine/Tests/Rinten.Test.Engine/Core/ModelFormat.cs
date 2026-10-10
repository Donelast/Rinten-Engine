using System.IO;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Our own model format, written and read back. A format is a promise between a writer and a reader, and the only way to know it is kept
//is to make the trip. These build a model through the core's ABI, save it, load it, and check that what comes out is what went in.
[TestClass]
public class ModelFormatTest
{
	private static string TempPath( string name )
		=> Path.Combine( Path.GetTempPath(), $"rintencore-test-{name}-{System.Guid.NewGuid():N}.mdl_c" );

	//The whole trip: geometry and collision in, a file, and the same numbers back out.
	[TestMethod]
	public unsafe void ModelSurvivesTheRoundTrip()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var path = TempPath( "roundtrip" );
		var builder = new CoreAssets.ModelBuilder();

		Assert.IsNotNull( builder, "the core should have made a builder" );

		try
		{
			builder.SetName( "models/test/roundtrip.mdl_c" );

			//A triangle, with values in every field so a layout mistake between the managed vertex and the one on disk cannot hide.
			var vertices = new Vertex[3];
			for ( var i = 0; i < vertices.Length; ++i )
			{
				vertices[i] = new Vertex
				{
					Position = new Vector3( i * 10, i * 20, i * 30 ),
					Normal = new Vector3( 0, 0, 1 ),
					Tangent = new Vector4( 1, 0, 0, -1 ),
					TexCoord0 = new Vector4( i, i + 1, i + 2, i + 3 ),
					TexCoord1 = new Vector4( i + 4, i + 5, i + 6, i + 7 ),
					Color = new Color32( (byte)(i * 3), (byte)(i * 5), (byte)(i * 7), 255 ),
				};
			}

			var indices = new uint[] { 0, 1, 2 };

			fixed ( Vertex* v = vertices )
			fixed ( uint* i = indices )
			{
				Assert.AreEqual( 0, builder.AddVertices( v, vertices.Length ) );
				Assert.AreEqual( 0, builder.AddIndices( i, indices.Length ) );
			}

			builder.AddMesh( "body", "materials/test.vmat", 0, 3, 0, 3 );

			//One collision part with one of each kind of shape, so every branch of the writer and the reader is walked.
			var transform = stackalloc float[7] { 1, 2, 3, 0, 0, 0, 1 };
			var part = builder.AddBody( "part0", "bone0", transform, 12.5f, 0.1f, 0.2f, 0.5f );

			Assert.AreEqual( 0, part, "the first body should be index zero" );

			var center = stackalloc float[3] { 4, 5, 6 };
			builder.SetMassCenter( part, center );

			var sphereAt = stackalloc float[3] { 7, 8, 9 };
			builder.AddSphere( part, "surfaces/metal.surface", sphereAt, 11.0f );

			var capA = stackalloc float[3] { 1, 0, 0 };
			var capB = stackalloc float[3] { -1, 0, 0 };
			builder.AddCapsule( part, "surfaces/flesh.surface", capA, capB, 3.0f );

			//A tetrahedron: the smallest thing that is a hull rather than a plane.
			var hull = stackalloc float[12] { 0, 0, 0, 10, 0, 0, 0, 10, 0, 0, 0, 10 };
			builder.AddHull( part, "surfaces/wood.surface", hull, 4 );

			var meshPoints = stackalloc float[9] { 0, 0, 0, 100, 0, 0, 0, 100, 0 };
			var meshIndices = stackalloc uint[3] { 0, 1, 2 };
			builder.AddCollisionMesh( part, "surfaces/concrete.surface", meshPoints, 3, meshIndices, 3 );

			Assert.AreEqual( 1, builder.Save( path ), "the model should have been written" );
			Assert.IsTrue( File.Exists( path ), "the file should be on disk" );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( path );
		Assert.IsNotNull( model, "the model should load back" );

		try
		{
			Assert.AreEqual( "models/test/roundtrip.mdl_c", model.Name );

			//-- geometry --------------------------------------------------------

			Assert.AreEqual( 3, model.VertexCount );
			Assert.AreEqual( 3, model.IndexCount );

			var read = new Vertex[3];
			fixed ( Vertex* v = read )
			{
				Assert.AreEqual( 3, model.CopyVertices( v, read.Length ) );
			}

			for ( var i = 0; i < read.Length; ++i )
			{
				Assert.AreEqual( new Vector3( i * 10, i * 20, i * 30 ), read[i].Position, $"vertex {i} position" );
				Assert.AreEqual( new Vector3( 0, 0, 1 ), read[i].Normal, $"vertex {i} normal" );
				Assert.AreEqual( new Vector4( 1, 0, 0, -1 ), read[i].Tangent, $"vertex {i} tangent" );
				Assert.AreEqual( new Vector4( i, i + 1, i + 2, i + 3 ), read[i].TexCoord0, $"vertex {i} texcoord0" );
				Assert.AreEqual( new Vector4( i + 4, i + 5, i + 6, i + 7 ), read[i].TexCoord1, $"vertex {i} texcoord1" );
				Assert.AreEqual( (byte)(i * 5), read[i].Color.g, $"vertex {i} colour" );
			}

			//Bounds were not given, so they should be the box around the vertices.
			var bounds = stackalloc float[6];
			model.GetRenderBounds( bounds );

			Assert.AreEqual( 0.0f, bounds[0], 0.001f, "bounds min x" );
			Assert.AreEqual( 20.0f, bounds[3], 0.001f, "bounds max x" );
			Assert.AreEqual( 60.0f, bounds[5], 0.001f, "bounds max z" );

			Assert.AreEqual( 1, model.MeshCount );
			Assert.AreEqual( "body", model.MeshName( 0 ) );
			Assert.AreEqual( "materials/test.vmat", model.MeshMaterial( 0 ) );

			//-- collision -------------------------------------------------------

			Assert.AreEqual( 1, model.BodyCount );
			Assert.AreEqual( "part0", model.BodyName( 0 ) );
			Assert.AreEqual( "bone0", model.BodyBone( 0 ) );

			var readTransform = stackalloc float[7];
			model.BodyTransform( 0, readTransform );
			Assert.AreEqual( 2.0f, readTransform[1], 0.001f, "body transform y" );
			Assert.AreEqual( 1.0f, readTransform[6], 0.001f, "body rotation w" );

			var properties = stackalloc float[4];
			model.BodyProperties( 0, properties );
			Assert.AreEqual( 12.5f, properties[0], 0.001f, "mass" );
			Assert.AreEqual( 0.5f, properties[3], 0.001f, "gravity scale" );

			var readCenter = stackalloc float[3];
			Assert.AreEqual( 1, model.BodyMassCenter( 0, readCenter ), "mass centre was set" );
			Assert.AreEqual( 5.0f, readCenter[1], 0.001f, "mass centre y" );

			Assert.AreEqual( 4, model.ShapeCount( 0 ), "one of each kind" );

			Assert.AreEqual( (int)ModelFormat.ShapeKind.Sphere, model.ShapeKind( 0, 0 ) );
			Assert.AreEqual( (int)ModelFormat.ShapeKind.Capsule, model.ShapeKind( 0, 1 ) );
			Assert.AreEqual( (int)ModelFormat.ShapeKind.Hull, model.ShapeKind( 0, 2 ) );
			Assert.AreEqual( (int)ModelFormat.ShapeKind.Mesh, model.ShapeKind( 0, 3 ) );

			Assert.AreEqual( "surfaces/metal.surface", model.ShapeSurface( 0, 0 ) );
			Assert.AreEqual( "surfaces/concrete.surface", model.ShapeSurface( 0, 3 ) );

			var primitive = stackalloc float[7];
			model.ShapePrimitive( 0, 0, primitive );
			Assert.AreEqual( 8.0f, primitive[1], 0.001f, "sphere centre y" );
			Assert.AreEqual( 11.0f, primitive[3], 0.001f, "sphere radius" );

			model.ShapePrimitive( 0, 1, primitive );
			Assert.AreEqual( -1.0f, primitive[3], 0.001f, "capsule second end x" );
			Assert.AreEqual( 3.0f, primitive[6], 0.001f, "capsule radius" );

			//The hull's points, and the mesh's points and indices, come back as they went in - and
			//each shape sees only its own, which is what the shared point block has to get right.
			Assert.AreEqual( 4, model.ShapePointCount( 0, 2 ), "hull points" );
			Assert.AreEqual( 0, model.ShapeIndexCount( 0, 2 ), "a hull has no indices" );

			var hullPoints = stackalloc float[12];
			Assert.AreEqual( 12, model.ShapeCopyPoints( 0, 2, hullPoints, 12 ) );
			Assert.AreEqual( 10.0f, hullPoints[3], 0.001f, "second hull point x" );
			Assert.AreEqual( 10.0f, hullPoints[11], 0.001f, "fourth hull point z" );

			Assert.AreEqual( 3, model.ShapePointCount( 0, 3 ), "mesh points" );
			Assert.AreEqual( 3, model.ShapeIndexCount( 0, 3 ), "mesh indices" );

			var readMeshPoints = stackalloc float[9];
			Assert.AreEqual( 9, model.ShapeCopyPoints( 0, 3, readMeshPoints, 9 ) );
			Assert.AreEqual( 100.0f, readMeshPoints[3], 0.001f, "second mesh point x" );
		}
		finally
		{
			model.Dispose();
			File.Delete( path );
		}
	}

	//A file that is not one of ours, or is one of ours but damaged, has to be refused rather than read as whatever the bytes happen to say.
	[TestMethod]
	public void RubbishIsRefused()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		Assert.IsNull( CoreAssets.ModelFile.Load( "no/such/file.mdl_c" ), "a missing file is not a model" );

		var path = TempPath( "rubbish" );

		try
		{
			//Long enough to be a header, and entirely the wrong thing.
			File.WriteAllBytes( path, new byte[512] );
			Assert.IsNull( CoreAssets.ModelFile.Load( path ), "zeroes are not a model" );

			File.WriteAllText( path, "this is not a model, it is a sentence about one" );
			Assert.IsNull( CoreAssets.ModelFile.Load( path ), "text is not a model" );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//Half a file is the dangerous case: the header is intact and says how big the model is, so every offset in it points past the end.
	[TestMethod]
	public unsafe void TruncatedFilesAreRefused()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var path = TempPath( "truncated" );
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			builder.SetName( "models/test/truncated.mdl_c" );

			var vertices = new Vertex[64];
			fixed ( Vertex* v = vertices )
			{
				builder.AddVertices( v, vertices.Length );
			}

			Assert.AreEqual( 1, builder.Save( path ) );

			var whole = File.ReadAllBytes( path );
			Assert.IsTrue( whole.Length > 128, "the model should be big enough to cut in half" );

			File.WriteAllBytes( path, whole[..(whole.Length / 2)] );

			Assert.IsNull( CoreAssets.ModelFile.Load( path ), "half a model is not a model" );
		}
		finally
		{
			builder.Dispose();
			File.Delete( path );
		}
	}
}
