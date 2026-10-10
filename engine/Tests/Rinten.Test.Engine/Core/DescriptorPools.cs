using System;
using System.IO;

namespace EngineTests.Core;

//Descriptor pools grow: more objects than the first pool holds all draw, with validation on
[TestClass]
[DoNotParallelize]
public class DescriptorPoolTests
{
	//A shader that reads nothing and draws nothing, so that what is being measured is the set a pulled draw needs and not what it does with it.
	private const string Source = """
		struct V2F
		{
			float4 position : SV_Position;
		};

		[shader("vertex")]
		V2F MainVs( uint id : SV_VertexID )
		{
			V2F o;

			// Degenerate on purpose: the point of the draw is the descriptor set
			// it has to be given, not the pixels.
			o.position = float4( 0.0, 0.0, 0.0, 1.0 );
			return o;
		}

		[shader("fragment")]
		float4 MainPs( V2F i ) : SV_Target
		{
			return float4( 1.0, 0.0, 1.0, 1.0 );
		}
		""";

	[TestMethod]
	public unsafe void MoreObjectsThanTheFirstPoolHolds()
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

		//What the core's first pool holds, plus enough to be sure of crossing it.
		const int Objects = 2048 + 64;

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-pool-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var view = 0ul;
		var pipeline = 0ul;
		var buffers = new ulong[Objects];

		try
		{
			var path = Path.Combine( directory, "pulled.shader" );
			File.WriteAllText( path, Source );

			var stages = CoreShaderFile.Compile( path );

			if ( stages is null )
			{
				Assert.Inconclusive( $"the shader would not compile: {CoreRenderDevice.ShaderError}" );
				return;
			}

			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view );

			//No vertex layout: a pulled draw binds no vertex buffer, it hands the shader a buffer to index.
			pipeline = CoreRenderDevice.CreatePipeline(
				stages[(int)CoreShaderFile.Stage.Vertex],
				stages[(int)CoreShaderFile.Stage.Pixel] );

			Assert.AreNotEqual( 0ul, pipeline, $"the pipeline should bake: {CoreRenderDevice.Description}" );

			//Sixteen bytes each: what is being asked for is a set per buffer, not memory.
			//Every one is its own buffer, so every one is its own pair and its own set.
			var bytes = new byte[16];

			for ( var i = 0; i < Objects; i++ )
			{
				bytes[0] = (byte)i;
				buffers[i] = CoreRenderDevice.CreateBuffer( bytes );

				Assert.AreNotEqual( 0ul, buffers[i],
					$"buffer {i} of {Objects} should exist: {CoreRenderDevice.Description}" );
			}

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ),
				$"the view would not begin: {CoreRenderDevice.Description}" );

			var drawn = 0;

			for ( var i = 0; i < Objects; i++ )
			{
				if ( CoreRenderDevice.DrawPulled( pipeline, buffers[i], 0, 3 ) ) drawn++;
			}

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			Assert.AreEqual( Objects, drawn,
				$"a draw stopped happening after the first pool filled up: {CoreRenderDevice.Description}" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			foreach ( var buffer in buffers ) CoreRenderDevice.DestroyBuffer( buffer );

			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			Directory.Delete( directory, true );
		}
	}
}
