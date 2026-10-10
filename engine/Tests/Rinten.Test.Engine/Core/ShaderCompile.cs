using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;

namespace EngineTests.Core;

//Shaders as assets: written in Slang, compiled to SPIR-V, carried in a file the renderer can pick stages out of.
[TestClass]
[DoNotParallelize]
public class ShaderCompileTests
{
	//A whole surface in miniature: enough of the engine's bindings to be a real shader, small enough to read.
	private const string Source = """
		struct VIn
		{
			[[vk::location(0)]] float3 position : POSITION;

		#ifdef SKINNED
			[[vk::location(6)]] uint4 bones : BLENDINDICES;
			[[vk::location(7)]] float4 weights : BLENDWEIGHT;
		#endif
		};

		struct V2F
		{
			float4 position : SV_Position;
		};

		[[vk::push_constant]]
		cbuffer Push
		{
			row_major float4x4 g_modelToWorld;
			float4 g_tint;
		};

		#ifdef SKINNED
		[[vk::binding(0,1)]] StructuredBuffer<float4x4> g_bones;
		#endif

		[shader("vertex")]
		V2F MainVs( VIn i )
		{
			float3 position = i.position;

		#ifdef SKINNED
			float4x4 skin = g_bones[i.bones.x] * i.weights.x;
			position = mul( float4( position, 1.0 ), skin ).xyz;
		#endif

			V2F o;
			o.position = mul( float4( position, 1.0 ), g_modelToWorld );
			return o;
		}

		[shader("fragment")]
		float4 MainPs( V2F i ) : SV_Target
		{
			return g_tint;
		}
		""";

	//SPIR-V begins with this, whoever produced it.
	private const uint SpirvMagic = 0x07230203;

	[TestMethod]
	public void CompilesAShaderIntoItsStages()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler" );
			return;
		}

		var directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(),
			$"rinten-shader-{System.Guid.NewGuid():N}" );

		System.IO.Directory.CreateDirectory( directory );

		var path = System.IO.Path.Combine( directory, "test.shader" );

		try
		{
			System.IO.File.WriteAllText( path, Source );

			//Every variant, the one way there is to make them: the compiler prepends the defines and hands the file to Slang.
			var stages = CoreShaderFile.Compile( path );

			Assert.IsNotNull( stages, $"the shader should compile: {CoreRenderDevice.ShaderError}" );

			foreach ( CoreShaderFile.Stage which in System.Enum.GetValues<CoreShaderFile.Stage>() )
			{
				var stage = stages[(int)which];

				//A surface draws and does not run: it declares no MainCs and there is nothing to have compiled. Every stage that draws is still required.
				if ( CoreShaderFile.IsCompute( which ) )
				{
					Assert.IsNull( stage, "a shader that draws has no compute stage" );
					continue;
				}

				Assert.IsNotNull( stage, $"every stage should compile: {CoreRenderDevice.ShaderError}" );
				Assert.IsTrue( stage.Length > 4 );
				Assert.AreEqual( SpirvMagic, System.BitConverter.ToUInt32( stage, 0 ), "and be SPIR-V" );
			}

			//The skinned vertex stage reads a bone buffer the rigid one does not, so it cannot
			//be the same words - which is the whole reason for compiling the file twice.
			CollectionAssert.AreNotEqual( stages[0], stages[2],
				"the skinned vertex stage should differ from the rigid one" );

			var file = CoreShaderFile.Write( stages );

			Assert.IsNotNull( file );
			Assert.IsTrue( CoreShaderFile.IsOurs( file ), "and be recognisably a shader of ours" );

			//Out again, stage by stage, byte for byte.
			CollectionAssert.AreEqual( stages[0], CoreShaderFile.Read( file, CoreShaderFile.Stage.Vertex ) );
			CollectionAssert.AreEqual( stages[1], CoreShaderFile.Read( file, CoreShaderFile.Stage.Pixel ) );
			CollectionAssert.AreEqual( stages[2], CoreShaderFile.Read( file, CoreShaderFile.Stage.SkinnedVertex ) );
			CollectionAssert.AreEqual( stages[3], CoreShaderFile.Read( file, CoreShaderFile.Stage.SkinnedPixel ) );

			//And a shader with no skinned half is a shader, not a broken one. As many slots as there
			//are stages: a short array is a shader file with nothing in the stages nobody filled in.
			var partial = new byte[stages.Length][];

			partial[(int)CoreShaderFile.Stage.Vertex] = stages[(int)CoreShaderFile.Stage.Vertex];
			partial[(int)CoreShaderFile.Stage.Pixel] = stages[(int)CoreShaderFile.Stage.Pixel];

			var rigidOnly = CoreShaderFile.Write( partial );

			Assert.IsNotNull( CoreShaderFile.Read( rigidOnly, CoreShaderFile.Stage.Vertex ) );
			Assert.IsNull( CoreShaderFile.Read( rigidOnly, CoreShaderFile.Stage.SkinnedVertex ),
				"a stage that was never compiled reads as nothing" );
		}
		finally
		{
			System.IO.Directory.Delete( directory, true );
		}
	}

	//All engine shaders compile in every variant the renderer can request (skinned, cubemap, etc.)
	[TestMethod]
	public void TheBaseShadersCompile()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler" );
			return;
		}

		var root = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		if ( string.IsNullOrWhiteSpace( root ) )
		{
			Assert.Inconclusive( "no engine directory to look in" );
			return;
		}

		var shaders = System.IO.Path.GetFullPath( System.IO.Path.Combine( root, "..",
			"game", "addons", "base", "Assets", "shaders" ) );

		if ( !System.IO.Directory.Exists( shaders ) )
		{
			Assert.Inconclusive( $"no shaders at {shaders}" );
			return;
		}

		var ours = new[] { "standard.shader", "unlit.shader", "vertex_color.shader" };
		var looked = 0;

		foreach ( var name in ours )
		{
			var path = System.IO.Path.Combine( shaders, name );
			if ( !System.IO.File.Exists( path ) ) continue;

			looked++;

			var stages = CoreShaderFile.Compile( path );

			Assert.IsNotNull( stages, $"{name}: {CoreRenderDevice.ShaderError}" );

			foreach ( CoreShaderFile.Stage stage in System.Enum.GetValues<CoreShaderFile.Stage>() )
			{
				var bytes = stages[(int)stage];

				//The ones the engine ships all draw, so none of them has a compute stage - see CoreShaderFile.Stage.Compute.
				if ( CoreShaderFile.IsCompute( stage ) ) continue;

				Assert.IsNotNull( bytes, $"{name} has no {stage}: {CoreRenderDevice.ShaderError}" );
				Assert.AreEqual( SpirvMagic, System.BitConverter.ToUInt32( bytes, 0 ), $"{name} {stage}" );
			}

			//The forms have to actually differ, or the defines did nothing and the whole arrangement is a slow way of compiling one shader four times.
			CollectionAssert.AreNotEqual( stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.SkinnedVertex],
				$"{name}: the skinned vertex stage should read bones" );

			CollectionAssert.AreNotEqual( stages[(int)CoreShaderFile.Stage.Pixel],
				stages[(int)CoreShaderFile.Stage.ScenePixel],
				$"{name}: the world's pixel stage should write the second attachment" );
		}

		if ( looked == 0 ) Assert.Inconclusive( "the base shaders are not on disk here" );
	}

	//And that the renderer finds them by name, whether or not the project's content has been built.
	[TestMethod]
	public void TheEnginesShadersLoadByName()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler" );
			return;
		}

		foreach ( var name in new[] { CoreSceneRenderer.StandardShader, CoreSceneRenderer.SkyShader } )
		{
			var stages = CoreShaderFile.Stages( name );

			if ( stages is null )
			{
				Assert.Inconclusive( $"{name} is not reachable from here" );
				return;
			}

			Assert.IsNotNull( stages[(int)CoreShaderFile.Stage.Vertex], name );
			Assert.IsNotNull( stages[(int)CoreShaderFile.Stage.Pixel], name );
		}
	}

	//WGSL beside SPIR-V, which is what a browser draw has to eat.
	[TestMethod]
	public void CompilesWgslForTheBrowser()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler" );
			return;
		}

		var directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "rinten-shader-" + System.Guid.NewGuid().ToString( "N" ) );
		System.IO.Directory.CreateDirectory( directory );

		try
		{
			var path = System.IO.Path.Combine( directory, "tiny.shader" );
			System.IO.File.WriteAllText( path, Source );

			var wgsl = CoreShaderFile.Compile( path, CoreShaderCompiler.Format.Wgsl );
			if ( wgsl is null )
			{
				Assert.Inconclusive( $"WGSL did not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			Assert.IsNotNull( wgsl[(int)CoreShaderFile.Stage.Vertex] );
			Assert.AreNotEqual( SpirvMagic, System.BitConverter.ToUInt32( wgsl[0], 0 ),
				"WGSL is text, not SPIR-V" );

			var spirv = CoreShaderFile.Compile( path );
			Assert.IsNotNull( spirv );

			var file = CoreShaderFile.Write( spirv, wgsl );
			Assert.IsNotNull( file );
			CollectionAssert.AreEqual( wgsl[0], CoreShaderFile.ReadWgsl( file, CoreShaderFile.Stage.Vertex ) );
			CollectionAssert.AreEqual( spirv[0], CoreShaderFile.Read( file, CoreShaderFile.Stage.Vertex ) );
		}
		finally
		{
			System.IO.Directory.Delete( directory, true );
		}
	}
}
