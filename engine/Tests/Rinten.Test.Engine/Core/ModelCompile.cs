using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//Compiled models in the legacy resource container: reading our bytes out, and loading from memory
[TestClass]
[DoNotParallelize]
public unsafe class ModelCompileTests
{
	//A cube, compiled, as bytes.
	private static byte[] BuildCube()
	{
		CoreRenderDemo.BuildCubeMesh( out var vertices, out var indices );

		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(),
			$"rinten-compile-test-{System.Guid.NewGuid():N}.mdl_c" );

		try
		{
			fixed ( Vertex* v = vertices ) builder.AddVertices( v, vertices.Length );
			fixed ( uint* i = indices ) builder.AddIndices( i, indices.Length );

			var mesh = builder.AddMesh( "cube", "materials/first.mat", 0, indices.Length, 0, vertices.Length );

			Assert.AreEqual( 0, mesh, "the first mesh should be mesh zero" );

			//What the definition says about a surface beats what the export called it - the same overrule the editor's compiler applies.
			Assert.AreEqual( 1, builder.MeshCount );
			Assert.AreEqual( 1, builder.SetMeshMaterial( 0, "materials/second.mat" ) );
			Assert.AreEqual( 0, builder.SetMeshMaterial( 7, "materials/nowhere.mat" ),
				"a mesh that does not exist cannot be named" );

			Assert.AreEqual( 1, builder.Save( path ) );

			return System.IO.File.ReadAllBytes( path );
		}
		finally
		{
			builder.Dispose();
			System.IO.File.Delete( path );
		}
	}

	//A resource container around some bytes, laid out the way the legacy writer lays one out: a header, a table of named blocks, and the
	//payload in DATA. Both offsets in it are relative to where they were written, which is the part worth getting wrong once and never again.
	private static byte[] Wrap( byte[] payload )
	{
		const int headerSize = 16;
		const int entrySize = 12;

		var blocks = 1;
		var payloadAt = headerSize + ( entrySize * blocks );

		var file = new byte[payloadAt + payload.Length];

		System.BitConverter.GetBytes( (uint)file.Length ).CopyTo( file, 0 );
		System.BitConverter.GetBytes( (ushort)12 ).CopyTo( file, 4 );
		System.BitConverter.GetBytes( (ushort)0 ).CopyTo( file, 6 );

		//From the field itself: the table starts right after the header.
		System.BitConverter.GetBytes( (uint)( headerSize - 8 ) ).CopyTo( file, 8 );
		System.BitConverter.GetBytes( (uint)blocks ).CopyTo( file, 12 );

		System.Text.Encoding.ASCII.GetBytes( "DATA" ).CopyTo( file, headerSize );
		System.BitConverter.GetBytes( (uint)( payloadAt - ( headerSize + 4 ) ) ).CopyTo( file, headerSize + 4 );
		System.BitConverter.GetBytes( (uint)payload.Length ).CopyTo( file, headerSize + 8 );

		payload.CopyTo( file, payloadAt );

		return file;
	}

	//Reads a Wavefront OBJ
	[TestMethod]
	public void ReadsAWavefrontObj()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		//A quad as two triangles, with normals and texture coordinates, and a second material so there is something to cut into two meshes.
		var source = string.Join( "\n", new[]
		{
			"# a test quad",
			"v -1 0 -1",
			"v  1 0 -1",
			"v  1 0  1",
			"v -1 0  1",
			"vt 0 0",
			"vt 1 0",
			"vt 1 1",
			"vt 0 1",
			"vn 0 1 0",
			"o quad",
			"usemtl floor",
			"f 1/1/1 2/2/1 3/3/1",
			"usemtl edge",
			"f 1/1/1 3/3/1 4/4/1",
		} ).Replace( "\n", "\n" );

		var directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(),
			$"rinten-obj-{System.Guid.NewGuid():N}" );

		System.IO.Directory.CreateDirectory( directory );

		var path = System.IO.Path.Combine( directory, "quad.obj" );
		CoreAssets.ModelBuilder builder = null;

		try
		{
			System.IO.File.WriteAllText( path, source );

			builder = new CoreAssets.ModelBuilder();
			Assert.IsNotNull( builder );

			var meshes = CoreMeshImport.Import( builder, path, 1.0f );

			//One mesh per material, in the order the file names them.
			Assert.AreEqual( 2, meshes, "a material apiece" );
			Assert.AreEqual( 2, builder.MeshCount );

			var compiled = System.IO.Path.Combine( directory, "quad.mdl_c" );

			Assert.AreNotEqual( 0, builder.Save( compiled ) );

			var model = CoreAssets.ModelFile.Load( compiled );

			Assert.IsNotNull( model, "and it loads back" );

			try
			{
				//Four corners, not six: the two triangles share an edge, and a corner is one vertex however many faces use it.
				Assert.AreEqual( 4, model.VertexCount, "corners are shared" );
				Assert.AreEqual( 6, model.IndexCount );

				//The file's up is ours, so a quad lying flat in it lies flat here - no height at all, on Y.
				var bounds = stackalloc float[6];
				model.GetRenderBounds( bounds );

				Assert.AreEqual( 0.0f, bounds[1], 0.001f, "the flat quad has no bottom" );
				Assert.AreEqual( 0.0f, bounds[4], 0.001f, "and no top" );

				Assert.IsTrue( bounds[3] - bounds[0] > 1.0f, "and it is two units across" );
			}
			finally
			{
				model.Dispose();
			}
		}
		finally
		{
			if ( builder is not null ) builder.Dispose();

			System.IO.Directory.Delete( directory, true );
		}
	}

	[TestMethod]
	public void KeepsBodyGroupsAndMaterialSets()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		CoreRenderDemo.BuildCubeMesh( out var vertices, out var indices );

		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(),
			$"rinten-groups-{System.Guid.NewGuid():N}.mdl_c" );

		try
		{
			fixed ( Vertex* v = vertices ) builder.AddVertices( v, vertices.Length );
			fixed ( uint* i = indices ) builder.AddIndices( i, indices.Length );

			//Three meshes over the same geometry: a body, a hat, and a plume.
			builder.AddMesh( "body", "materials/body.mat", 0, 36, 0, 24 );
			builder.AddMesh( "hat", "materials/hat.mat", 0, 36, 0, 24 );
			builder.AddMesh( "plume", "materials/plume.mat", 0, 36, 0, 24 );

			Assert.AreEqual( "hat", builder.MeshName( 1 ) );

			var part = builder.AddPart( "headwear" );
			Assert.AreEqual( 0, part );

			//Bare head first, so it is what shows when nobody chooses.
			Assert.AreEqual( 0, builder.AddPartChoice( part, "none", 1ul ) );
			Assert.AreEqual( 1, builder.AddPartChoice( part, "hat", 2ul ) );
			Assert.AreEqual( 2, builder.AddPartChoice( part, "plumed", 4ul ) );

			builder.SetMeshGroup( 1, 2ul | 4ul );
			builder.SetMeshGroup( 2, 4ul );

			var group = builder.AddMaterialGroup( "winter" );
			Assert.AreEqual( 0, group );
			Assert.AreEqual( 1, builder.SetGroupMaterial( group, 1, "materials/snow.mat" ) );

			Assert.AreEqual( 1, builder.Save( path ) );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( path );
		Assert.IsNotNull( model, "a model with groups should still load" );

		try
		{
			Assert.AreEqual( 1ul, model.DefaultMask,
				"nothing chosen is the first choice of every group" );

			Assert.AreEqual( 0ul, model.MeshGroup( 0 ),
				"a mesh no choice claims belongs to no group" );

			Assert.AreEqual( 6ul, model.MeshGroup( 1 ) );
			Assert.AreEqual( 4ul, model.MeshGroup( 2 ) );

			Assert.AreEqual( 1, model.PartCount );
			Assert.AreEqual( "headwear", model.PartName( 0 ) );
			Assert.AreEqual( 7ul, model.PartMask( 0 ), "the group owns all its choices' bits" );
			Assert.AreEqual( 3, model.PartChoiceCount( 0 ) );
			Assert.AreEqual( "plumed", model.PartChoiceName( 0, 2 ) );
			Assert.AreEqual( 4ul, model.PartChoiceMask( 0, 2 ) );

			Assert.AreEqual( 1, model.MaterialGroupCount );
			Assert.AreEqual( "winter", model.MaterialGroupName( 0 ) );

			//What the group says where it says something, and the mesh's own material everywhere else.
			Assert.AreEqual( "materials/snow.mat", model.GroupMaterial( 0, 1 ) );
			Assert.AreEqual( "materials/body.mat", model.GroupMaterial( 0, 0 ) );
			Assert.AreEqual( "materials/plume.mat", model.GroupMaterial( 0, 2 ) );
		}
		finally
		{
			model.Dispose();
			System.IO.File.Delete( path );
		}
	}

	//Model.MeshInfo is filled in; the inspector and thumbnails rely on it
	[TestMethod]
	public void AModelKnowsHowMuchOfItThereIs()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		//One of the built-in shapes, so this needs nothing on disk.
		var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

		Assert.IsNotNull( model, "the built-in box should load" );

		var info = model.MeshInfo;

		Assert.IsNotNull( info, "a model with no mesh info is a model the inspector cannot draw" );

		Assert.AreEqual( model.MeshCount, info.Meshes.Length, "one entry per mesh" );
		Assert.IsTrue( info.TotalVertices > 0, $"a box has vertices - got {info.TotalVertices}" );
		Assert.IsTrue( info.TotalTriangles > 0, $"a box has triangles - got {info.TotalTriangles}" );

		foreach ( var mesh in info.Meshes )
		{
			Assert.IsNotNull( mesh.DrawCalls, "an empty list, not a null one - the inspector walks it" );
		}

		//Asked twice is the same answer, not a second count.
		Assert.AreSame( info, model.MeshInfo );
	}

	//Which meshes a mask shows.
	[TestMethod]
	public void ShowsTheMeshesAMaskClaims()
	{
		var always = new CoreSceneRenderer.MeshDraw( 0, 3, 0, 0, Color.White );
		var hat = new CoreSceneRenderer.MeshDraw( 0, 3, 0, 0, Color.White ) { Group = 6ul };
		var plume = new CoreSceneRenderer.MeshDraw( 0, 3, 0, 0, Color.White ) { Group = 4ul };

		//Bare head.
		Assert.IsTrue( CoreSceneRenderer.SceneMesh.Shows( always, 1ul ) );
		Assert.IsFalse( CoreSceneRenderer.SceneMesh.Shows( hat, 1ul ) );
		Assert.IsFalse( CoreSceneRenderer.SceneMesh.Shows( plume, 1ul ) );

		//Hat without the plume.
		Assert.IsTrue( CoreSceneRenderer.SceneMesh.Shows( hat, 2ul ) );
		Assert.IsFalse( CoreSceneRenderer.SceneMesh.Shows( plume, 2ul ) );

		//And with it.
		Assert.IsTrue( CoreSceneRenderer.SceneMesh.Shows( hat, 4ul ) );
		Assert.IsTrue( CoreSceneRenderer.SceneMesh.Shows( plume, 4ul ) );

		//A mesh nobody claimed is drawn whatever anyone chooses, including nothing at all - which is the case that would hide a whole model.
		Assert.IsTrue( CoreSceneRenderer.SceneMesh.Shows( always, 0ul ) );
	}

	//Models from an older format version are refused rather than misread
	[TestMethod]
	public void RefusesAModelFromAnOlderFormat()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var bytes = BuildCube();

		fixed ( byte* data = bytes )
		{
			Assert.IsNotNull( CoreAssets.ModelFile.LoadMemory( data, bytes.Length ),
				"the model as written should load" );
		}

		//The version sits right after the magic.
		var version = System.BitConverter.ToUInt32( bytes, 4 );
		Assert.IsTrue( version >= 3, $"the format should be at three or past it, not {version}" );

		System.BitConverter.GetBytes( version - 1 ).CopyTo( bytes, 4 );

		fixed ( byte* data = bytes )
		{
			Assert.IsNull( CoreAssets.ModelFile.LoadMemory( data, bytes.Length ),
				"a model from the version before this one should be refused" );
		}
	}

	//A built-in model that fails to load reports once instead of falling back to itself forever
	[TestMethod]
	public void ABuiltInThatWillNotLoadDoesNotAskItself()
	{
		//Before this was fixed, either of these never returned.
		var error = Model.Error;
		var cube = Model.Cube;

        Assert.IsTrue( error is null || error.IsValid(),
			"the error model is either there or it is not - never half of one" );

		Assert.IsTrue( cube is null || cube.IsValid() );
	}

	[TestMethod]
	public void LoadsAModelFromMemory()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var bytes = BuildCube();

		Assert.IsTrue( CoreMeshImport.IsOurs( bytes ), "a model we wrote should be recognisably ours" );

		fixed ( byte* data = bytes )
		{
			var model = CoreAssets.ModelFile.LoadMemory( data, bytes.Length );

			Assert.IsNotNull( model, "bytes in hand should load as readily as a file" );

			try
			{
				Assert.AreEqual( 24, model.VertexCount );
				Assert.AreEqual( 36, model.IndexCount );
			}
			finally
			{
				model.Dispose();
			}
		}
	}

	[TestMethod]
	public void ReadsAModelBackOutOfAContainer()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var bytes = BuildCube();
		var wrapped = Wrap( bytes );

		Assert.IsFalse( CoreMeshImport.IsOurs( wrapped ), "a container is not a model" );

		var inside = CoreMeshImport.Unwrap( wrapped );

		Assert.IsNotNull( inside, "the model should come back out of the container" );
		CollectionAssert.AreEqual( bytes, inside, "and come back the same model" );

		//What the editor writes today: the definition's own text, which is a perfectly good container holding something that is not a model.
		var text = System.Text.Encoding.UTF8.GetBytes( "{\"Mesh\":\"models/thing.fbx\"}" );

		Assert.IsNull( CoreMeshImport.Unwrap( Wrap( text ) ),
			"a container holding something else should be refused, not misread" );

		Assert.IsNull( CoreMeshImport.Unwrap( new byte[] { 1, 2, 3 } ),
			"and so should anything too small to be either" );
	}
}
