using System;
using System.IO;
using System.Linq;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Importing FBX, which is what source art usually is - and the only road by which a model's skeleton and skin weights reach our own format.
[TestClass]
public class FbxImportTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-fbx-{Guid.NewGuid():N}{extension}" );

	//Hand-written ASCII FBX 7.4: two bones, a four-vertex strip, each half bound to one bone
	private const string SkinnedFbx = """
		; FBX 7.4.0 project file
		FBXHeaderExtension:  {
			FBXHeaderVersion: 1003
			FBXVersion: 7400
		}
		GlobalSettings:  {
			Version: 1000
			Properties70:  {
				P: "UpAxis", "int", "Integer", "",1
				P: "UpAxisSign", "int", "Integer", "",1
				P: "FrontAxis", "int", "Integer", "",2
				P: "FrontAxisSign", "int", "Integer", "",1
				P: "CoordAxis", "int", "Integer", "",0
				P: "CoordAxisSign", "int", "Integer", "",1
				P: "UnitScaleFactor", "double", "Number", "",100
			}
		}
		Objects:  {
			Geometry: 1000, "Geometry::strip", "Mesh" {
				Vertices: *12 {
					a: -0.5,0,0, 0.5,0,0, -0.5,0,2, 0.5,0,2
				}
				PolygonVertexIndex: *6 {
					a: 0,1,-3, 2,1,-4
				}
				GeometryVersion: 124
				LayerElementNormal: 0 {
					Version: 102
					Name: ""
					MappingInformationType: "ByVertice"
					ReferenceInformationType: "Direct"
					Normals: *12 {
						a: 0,-1,0, 0,-1,0, 0,-1,0, 0,-1,0
					}
				}
				LayerElementUV: 0 {
					Version: 101
					Name: "map1"
					MappingInformationType: "ByVertice"
					ReferenceInformationType: "Direct"
					UV: *8 {
						a: 0,0, 1,0, 0,1, 1,1
					}
				}
				Layer: 0 {
					Version: 100
					LayerElement:  {
						Type: "LayerElementNormal"
						TypedIndex: 0
					}
					LayerElement:  {
						Type: "LayerElementUV"
						TypedIndex: 0
					}
				}
			}
			Model: 2000, "Model::strip", "Mesh" {
				Version: 232
			}
			Model: 3000, "Model::root", "LimbNode" {
				Version: 232
			}
			Model: 3001, "Model::upper", "LimbNode" {
				Version: 232
				Properties70:  {
					P: "Lcl Translation", "Lcl Translation", "", "A",0,0,100
				}
			}
			Deformer: 4000, "Deformer::skin", "Skin" {
				Version: 101
			}
			Deformer: 4001, "SubDeformer::rootCluster", "Cluster" {
				Version: 100
				Indexes: *2 {
					a: 0,1
				}
				Weights: *2 {
					a: 1,1
				}
				Transform: *16 {
					a: 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1
				}
				TransformLink: *16 {
					a: 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1
				}
			}
			Deformer: 4002, "SubDeformer::upperCluster", "Cluster" {
				Version: 100
				Indexes: *2 {
					a: 2,3
				}
				Weights: *2 {
					a: 1,1
				}
				Transform: *16 {
					a: 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1
				}
				TransformLink: *16 {
					a: 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,100,1
				}
			}
		}
		Connections:  {
			C: "OO",2000,0
			C: "OO",1000,2000
			C: "OO",3000,0
			C: "OO",3001,3000
			C: "OO",4000,1000
			C: "OO",4001,4000
			C: "OO",4002,4000
			C: "OO",3000,4001
			C: "OO",3001,4002
		}
		""";

	//The skeleton and the per-vertex weights survive the trip from FBX into our own format and back out of it.
	[TestMethod]
	public unsafe void ImportsSkinnedGeometry()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var fbx = TempPath( ".fbx" );
		var compiled = TempPath( ".mdl_c" );

		File.WriteAllText( fbx, SkinnedFbx );

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			var meshes = CoreFbxImport.ImportModel( builder, fbx, 1.0f );

			Assert.IsTrue( meshes > 0, "the FBX should have brought at least one mesh" );
			Assert.AreEqual( 1, builder.Save( compiled ) );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( compiled );
		Assert.IsNotNull( model, "the imported model should load back" );

		try
		{
			var vertexCount = model.VertexCount;

			Assert.IsTrue( vertexCount >= 4, $"expected the strip's vertices, got {vertexCount}" );

			//Two bones, in the file's own order, with the hierarchy intact.
			Assert.AreEqual( 2, model.BoneCount, "both bones should have come across" );

			var names = new[]
			{
				model.BoneName( 0 ),
				model.BoneName( 1 ),
			};

			CollectionAssert.AreEquivalent( new[] { "root", "upper" }, names,
				$"got {string.Join( ", ", names )}" );

			var upper = Array.IndexOf( names, "upper" );
			var root = Array.IndexOf( names, "root" );

			Assert.AreEqual( root, model.BoneParent( upper ),
				"upper hangs off root" );
			Assert.AreEqual( -1, model.BoneParent( root ),
				"root hangs off nothing" );

			//Every vertex is skinned, and every one of them fully owned by a single bone - which is how the file binds them.
			Assert.AreEqual( vertexCount, model.SkinCount,
				"the skin should cover every vertex" );

			var skin = new byte[vertexCount * 8];
			fixed ( byte* s = skin ) model.CopySkin( s, vertexCount );

			var vertices = new Vertex[vertexCount];
			fixed ( Vertex* v = vertices ) model.CopyVertices( v, vertexCount );

			for ( var i = 0; i < vertexCount; i++ )
			{
				var weights = 0;
				for ( var w = 0; w < 4; w++ ) weights += skin[( i * 8 ) + 4 + w];

				Assert.AreEqual( 255, weights, $"vertex {i}'s weights should sum to 255" );
			}

			//Which way the strip ended up pointing is the axis conversion's business, and not what this test
			//is about - so it finds the axis the strip actually runs along and checks the binding across it.
			var along = -1;
			var widest = 0.0f;

			for ( var axis = 0; axis < 3; axis++ )
			{
				var low = float.MaxValue;
				var high = float.MinValue;

				foreach ( var vertex in vertices )
				{
					var value = axis switch { 0 => vertex.Position.x, 1 => vertex.Position.y, _ => vertex.Position.z };

					low = MathF.Min( low, value );
					high = MathF.Max( high, value );
				}

				if ( high - low <= widest ) continue;

				widest = high - low;
				along = axis;
			}

			Assert.IsTrue( widest > 0.0001f, "the strip should have some length to it" );

			float Along( Vertex vertex )
				=> along switch { 0 => vertex.Position.x, 1 => vertex.Position.y, _ => vertex.Position.z };

			var middle = ( vertices.Min( Along ) + vertices.Max( Along ) ) * 0.5f;

			int? nearBone = null;
			int? farBone = null;

			for ( var i = 0; i < vertexCount; i++ )
			{
				var bone = (int)skin[i * 8];
				var near = Along( vertices[i] ) < middle;

				if ( near ) nearBone ??= bone;
				else farBone ??= bone;

				Assert.AreEqual( near ? nearBone.Value : farBone.Value, bone,
					$"vertex {i} sits with the others on its end of the strip, so it should share their bone" );
			}

			Assert.IsNotNull( nearBone );
			Assert.IsNotNull( farBone );
			Assert.AreNotEqual( nearBone.Value, farBone.Value, "the two ends belong to different bones" );

			CollectionAssert.AreEquivalent( new[] { root, upper }, new[] { nearBone.Value, farBone.Value },
				"and between them they are root and upper" );
		}
		finally
		{
			model.Dispose();
			File.Delete( fbx );
			File.Delete( compiled );
		}
	}

	//A file that is not an FBX says so rather than producing an empty model.
	[TestMethod]
	public void RefusesWhatIsNotAnFbx()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var path = TempPath( ".fbx" );
		File.WriteAllText( path, "this is not an FBX file" );

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			Assert.AreEqual( -1, CoreFbxImport.ImportModel( builder, path, 1.0f ) );
			Assert.AreEqual( -1, CoreFbxImport.ImportModel( builder, "no/such/file.fbx", 1.0f ) );
		}
		finally
		{
			builder.Dispose();
			File.Delete( path );
		}
	}
}
