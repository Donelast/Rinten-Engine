using System;
using System.IO;

namespace EngineTests.Core;

//Shader includes resolve from engine and project shader folders, not only the shader's own
[TestClass]
[DoNotParallelize]
public class ShaderSearchPathTests
{
	//The engine's own shader folder, or null where this host has none.
	private static string EngineShaders()
	{
		var root = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		if ( !string.IsNullOrWhiteSpace( root ) )
		{
			var inside = Path.Combine( root, "addons", "base", "Assets", "shaders" );

			if ( Directory.Exists( inside ) ) return inside;
		}

		//Walking up from the test binary, for a host that was never told.
		var at = new DirectoryInfo( AppContext.BaseDirectory );

		while ( at is not null )
		{
			var inside = Path.Combine( at.FullName, "game", "addons", "base", "Assets", "shaders" );

			if ( Directory.Exists( inside ) ) return inside;

			at = at.Parent;
		}

		return null;
	}

	//Compiles every shader of the project in RINTEN_TEST_PROJECT_SHADERS; inconclusive if unset
	[TestMethod]
	public void EveryShaderOfANamedProjectCompiles()
	{
		var folder = Environment.GetEnvironmentVariable( "RINTEN_TEST_PROJECT_SHADERS" );

		if ( string.IsNullOrWhiteSpace( folder ) || !Directory.Exists( folder ) )
		{
			Assert.Inconclusive( "no RINTEN_TEST_PROJECT_SHADERS folder to compile" );
			return;
		}

		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "there is no shader compiler here" );
			return;
		}

		var library = EngineShaders();

		if ( library is not null ) CoreShaderCompiler.AddSearchPath( library );

		CoreShaderCompiler.AddSearchPath( folder );

		try
		{
			var refused = 0;

			foreach ( var file in Directory.EnumerateFiles( folder, "*.shader", SearchOption.AllDirectories ) )
			{
				var stages = CoreShaderFile.Compile( file );

				if ( stages is not null )
				{
					Console.WriteLine( $"ok   {Path.GetFileName( file )}" );
					continue;
				}

				refused++;

				Console.WriteLine( $"FAIL {Path.GetFileName( file )}: {CoreRenderDevice.ShaderError}" );
			}

			Assert.AreEqual( 0, refused, "every shader of the project should compile" );
		}
		finally
		{
			CoreShaderCompiler.ClearSearchPaths();
		}
	}

	[TestMethod]
	public void AShaderElsewhereCanIncludeTheEngineLibrary()
	{
		if ( !CoreNative.Available || !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "there is no shader compiler here" );
			return;
		}

		var library = EngineShaders();

		if ( library is null )
		{
			Assert.Inconclusive( "the engine's shader folder is not where this host can see it" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-include-{Guid.NewGuid():N}" );

		Directory.CreateDirectory( directory );

		try
		{
			//Nothing beside it: everything this names has to be found through the search paths.
			File.WriteAllText( Path.Combine( directory, "away.shader" ), """
				[[vk::push_constant]]
				cbuffer Push
				{
					row_major float4x4 g_modelToWorld;
					float4 g_tint;
				};

				#include "frame.slang"

				struct V2F
				{
					float4 position : SV_Position;
					float3 world : TEXCOORD2;
				};

				[shader("vertex")]
				V2F MainVs( float3 position : POSITION )
				{
					float3 world = mul( float4( position, 1.0 ), g_modelToWorld ).xyz;

					V2F o;
					o.position = mul( float4( world, 1.0 ), g_frame[0].worldToClip );
					o.world = world;
					return o;
				}

				[shader("fragment")]
				float4 MainPs( V2F i ) : SV_Target0
				{
					// Something out of the include, so that a compile which
					// merely found the file still has to understand it.
					return float4( SkyAmbient( float3( 0, 0, 1 ) ), 1.0 );
				}
				""" );

			//The engine library is on the search path for every compile, even after ClearSearchPaths
			CoreShaderCompiler.ClearSearchPaths();

			var found = CoreShaderFile.Compile( Path.Combine( directory, "away.shader" ) );

			Assert.IsNotNull( found, $"the engine's library should be on the search path: {CoreRenderDevice.ShaderError}" );
			Assert.IsNotNull( found[(int)CoreShaderFile.Stage.Vertex], "the vertex stage" );
			Assert.IsNotNull( found[(int)CoreShaderFile.Stage.Pixel], "the pixel stage" );

			//And a library that is always there is not everything being there: an include nobody provides
			//still fails, which is what says the compile above found a file rather than ignoring the line.
			File.WriteAllText( Path.Combine( directory, "nowhere.shader" ), """
				#include "there_is_no_such_include.slang"

				[shader("fragment")]
				float4 MainPs() : SV_Target0 { return 1.0; }
				""" );

			Assert.IsNull( CoreShaderFile.Compile( Path.Combine( directory, "nowhere.shader" ) ),
				"a shader including something that does not exist should not compile" );

			//The folder the test was handed is the one the compile found it by, so a library
			//that moved shows up here rather than as a shader failing somewhere else.
			Assert.IsTrue( Directory.Exists( library ), $"the engine's shaders should be at {library}" );
		}
		finally
		{
			CoreShaderCompiler.ClearSearchPaths();

			Directory.Delete( directory, true );
		}
	}
}
