using System;
using System.Collections.Generic;
using System.IO;

namespace EngineTests.Core;

//A feature compiles a separate shader variant; on and off must render different pixels
[TestClass]
[DoNotParallelize]
public class ShaderFeatureTests
{
	private const int Size = 32;

	//A shader with a switch in it, written in the legacy shader format.
	private const string Source = """
		FEATURES
		{
			Feature( F_RED, 0..1, "Colour" );
			Feature( F_MOOD, 0..2 ( 0="Flat", 1="Bright", 2="Dark" ), "Colour" );
		}

		struct VIn
		{
			[[vk::location(0)]] float3 position : POSITION;
		};

		[shader("vertex")]
		float4 MainVs( VIn i ) : SV_Position
		{
			return float4( i.position.xy * 4.0, 0.5, 1.0 );
		}

		[shader("fragment")]
		float4 MainPs() : SV_Target
		{
		#if F_RED
			return float4( 1.0, 0.0, 0.0, 1.0 );
		#else
			return float4( 0.0, 1.0, 0.0, 1.0 );
		#endif
		}
		""";

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void AShaderSaysWhatItCanBeBuiltWithout()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"rinten-features-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		try
		{
			var path = Path.Combine( directory, "switched.shader" );
			File.WriteAllText( path, Source );

			var features = CoreShaderFeatures.Of( path );

			Assert.AreEqual( 2, features.Length, "both features should have been read" );

			Assert.AreEqual( "F_RED", features[0].Name );
			Assert.AreEqual( "Colour", features[0].Group );
			Assert.IsTrue( features[0].IsSwitch, "nought to one is a tick" );

			Assert.AreEqual( "F_MOOD", features[1].Name );
			Assert.AreEqual( 2, features[1].Most );
			Assert.IsFalse( features[1].IsSwitch, "nought to two is a list" );
			Assert.AreEqual( "Dark", features[1].NameOf( 2 ), "the shader named its values" );

			//In the order declared, which is what makes the combo readable from either end.
			Assert.AreEqual( "0.0", CoreShaderFeatures.Combo( features, null ) );
			Assert.AreEqual( "1.2", CoreShaderFeatures.Combo( features,
				new Dictionary<string, int> { ["F_RED"] = 1, ["F_MOOD"] = 5 } ),
				"a value past what the shader allows is brought back inside it" );

			//And the block itself never reaches a compiler that would not know what to do with it -
			//but the lines it took up are still there, so an error on line thirty is on line thirty.
			var without = CoreShaderFeatures.Without( Source );

			Assert.IsFalse( without.Contains( "Feature(", StringComparison.Ordinal ),
				"the features block should have been cut out" );

			Assert.AreEqual( Source.Split( '\n' ).Length, without.Split( '\n' ).Length,
				"cutting the block moved the lines under it" );
		}
		finally
		{
			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	[TestMethod]
	public void TurningAFeatureOnBuildsADifferentShader()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-features-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var view = 0ul;
		var vertices = 0ul;
		var indices = 0ul;
		var off = 0ul;
		var on = 0ul;

		try
		{
			var path = Path.Combine( directory, "switched.shader" );
			File.WriteAllText( path, Source );

			var without = CoreShaderFile.Compile( path, CoreShaderCompiler.Format.Spirv, "0.0" );
			var with = CoreShaderFile.Compile( path, CoreShaderCompiler.Format.Spirv, "1.0" );

			if ( without is null || with is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			//The two builds are not the same bytes. That alone is most of the claim: the same source with a define changed came out different.
			CollectionAssert.AreNotEqual( without[(int)CoreShaderFile.Stage.Pixel],
				with[(int)CoreShaderFile.Stage.Pixel],
				"the feature changed nothing about what was compiled" );

			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( Size, Size );
			Assert.AreNotEqual( 0ul, view );

			off = CoreRenderDevice.CreatePipeline( without[(int)CoreShaderFile.Stage.Vertex],
				without[(int)CoreShaderFile.Stage.Pixel], CoreRender.VertexLayoutKind.Engine );

			on = CoreRenderDevice.CreatePipeline( with[(int)CoreShaderFile.Stage.Vertex],
				with[(int)CoreShaderFile.Stage.Pixel], CoreRender.VertexLayoutKind.Engine );

			Assert.AreNotEqual( 0ul, off, $"the plain build should bake: {CoreRenderDevice.Description}" );
			Assert.AreNotEqual( 0ul, on, $"the switched build should bake: {CoreRenderDevice.Description}" );

			CoreRenderDemo.BuildCubeMesh( out var mesh, out var indexList );

			vertices = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( mesh.AsSpan() ) );
			indices = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( indexList.AsSpan() ) );

			var errorsBefore = CoreErrors.Count;

			(int Red, int Green) Middle( ulong pipeline )
			{
				var pixels = new byte[Size * Size * 4];

				Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

				try
				{
					Assert.IsTrue( CoreRenderDevice.DrawIndexed( pipeline, vertices, indices,
						indexList.Length ), $"the draw was refused: {CoreRenderDevice.Description}" );
				}
				finally
				{
					CoreRenderDevice.EndViewFrame( view );
				}

				Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

				var at = ( ( ( Size / 2 ) * Size ) + ( Size / 2 ) ) * 4;

				return ( pixels[at], pixels[at + 1] );
			}

			var plain = Middle( off );
			var switched = Middle( on );

			Assert.IsTrue( plain.Green > 200 && plain.Red < 40,
				$"with the feature off the shader draws green, not {plain.Red}/{plain.Green}" );

			Assert.IsTrue( switched.Red > 200 && switched.Green < 40,
				$"with the feature on the shader draws red, not {switched.Red}/{switched.Green}" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyBuffer( vertices );
			CoreRenderDevice.DestroyBuffer( indices );
			CoreRenderDevice.DestroyPipeline( off );
			CoreRenderDevice.DestroyPipeline( on );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			try { Directory.Delete( directory, true ); } catch { }
		}
	}
}
