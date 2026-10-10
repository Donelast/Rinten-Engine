using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace ResourceTests;

//CompileBaked rebuilds from its last output: the Transform node must apply, and only once
[TestClass]
public unsafe class ModelDefinitionCompilerBakedTests
{
	static readonly JsonSerializerOptions WriteOptions = new()
	{
		IncludeFields = true,
		Converters = { new JsonStringEnumConverter() },
	};

	static string TempDir()
	{
		var dir = Path.Combine( Path.GetTempPath(), $"rinten-baked-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( dir );
		return dir;
	}

	//A single triangle, standing in for whatever a port baked in - CompileBaked doesn't
	//care what the geometry is, only that it's already there with no mesh source behind it.
	static void WriteBakedGeometry( string compiledPath )
	{
		var builder = new CoreAssets.ModelBuilder();
		Assert.IsNotNull( builder );

		try
		{
			var vertices = new[]
			{
				new Vertex { Position = new Vector3( 0, 0, 0 ), Normal = new Vector3( 0, 0, 1 ), Tangent = new Vector4( 1, 0, 0, -1 ) },
				new Vertex { Position = new Vector3( 1, 0, 0 ), Normal = new Vector3( 0, 0, 1 ), Tangent = new Vector4( 1, 0, 0, -1 ) },
				new Vertex { Position = new Vector3( 0, 1, 0 ), Normal = new Vector3( 0, 0, 1 ), Tangent = new Vector4( 1, 0, 0, -1 ) },
			};
			var indices = new uint[] { 0, 1, 2 };

			fixed ( Vertex* v = vertices ) builder.AddVertices( v, vertices.Length );
			fixed ( uint* i = indices ) builder.AddIndices( i, indices.Length );

			builder.AddMesh( "geo", "materials/default.mat", 0, indices.Length, 0, vertices.Length );

			Directory.CreateDirectory( Path.GetDirectoryName( compiledPath )! );
			Assert.AreEqual( 1, builder.Save( compiledPath ) );
		}
		finally
		{
			builder.Dispose();
		}
	}

	static Vector3[] ReadVertexPositions( string compiledPath )
	{
		var model = CoreAssets.ModelFile.Load( compiledPath );
		Assert.IsNotNull( model, $"{compiledPath} did not load back" );

		try
		{
			var count = model.VertexCount;
			var vertices = new Vertex[count];

			fixed ( Vertex* v = vertices )
				model.CopyVertices( v, count );

			var positions = new Vector3[count];
			for ( var i = 0; i < count; i++ ) positions[i] = vertices[i].Position;

			return positions;
		}
		finally
		{
			model.Dispose();
		}
	}

	[TestMethod]
	public void CompileBakedAppliesTheTransform()
	{
		var dir = TempDir();

		try
		{
			var definitionPath = Path.Combine( dir, "thing.mdl" );
			var compiledPath = Path.ChangeExtension( definitionPath, ".mdl_c" );

			WriteBakedGeometry( compiledPath );

			var definition = new ModelDefinition();
			definition.Transform.Enabled = true;
			definition.Transform.Scale = new Vector3( 2, 2, 2 );

			File.WriteAllText( definitionPath, JsonSerializer.Serialize( definition, WriteOptions ) );

			Assert.IsTrue( ModelDefinitionCompiler.Compile( definition, definitionPath ) );

			var positions = ReadVertexPositions( compiledPath );

			Assert.AreEqual( 2f, positions[1].x, 0.001f,
				"a baked model's geometry should pick up the whole-model Transform's scale" );
		}
		finally
		{
			Directory.Delete( dir, recursive: true );
		}
	}

	[TestMethod]
	public void CompileBakedDoesNotCompoundTheTransformOnASecondCompile()
	{
		var dir = TempDir();

		try
		{
			var definitionPath = Path.Combine( dir, "thing.mdl" );
			var compiledPath = Path.ChangeExtension( definitionPath, ".mdl_c" );

			WriteBakedGeometry( compiledPath );

			var definition = new ModelDefinition();
			definition.Transform.Enabled = true;
			definition.Transform.Scale = new Vector3( 2, 2, 2 );

			File.WriteAllText( definitionPath, JsonSerializer.Serialize( definition, WriteOptions ) );

			Assert.IsTrue( ModelDefinitionCompiler.Compile( definition, definitionPath ) );
			var afterFirst = ReadVertexPositions( compiledPath );

			//Same definition, unchanged Transform - as a live-recompile tick in the editor would fire without the user having touched anything.
			Assert.IsTrue( ModelDefinitionCompiler.Compile( definition, definitionPath ) );
			var afterSecond = ReadVertexPositions( compiledPath );

			for ( var i = 0; i < afterFirst.Length; i++ )
			{
				Assert.AreEqual( afterFirst[i].x, afterSecond[i].x, 0.001f, $"vertex {i}.x compounded on a second compile" );
				Assert.AreEqual( afterFirst[i].y, afterSecond[i].y, 0.001f, $"vertex {i}.y compounded on a second compile" );
				Assert.AreEqual( afterFirst[i].z, afterSecond[i].z, 0.001f, $"vertex {i}.z compounded on a second compile" );
			}
		}
		finally
		{
			Directory.Delete( dir, recursive: true );
		}
	}
}
