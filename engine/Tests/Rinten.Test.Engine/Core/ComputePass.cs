using System;
using System.IO;

namespace EngineTests.Core;

//Compute passes end to end: Slang compute source compiled, dispatched in a frame, result read back
[TestClass]
[DoNotParallelize]
public unsafe class ComputePassTests
{
	//Samples a picture and writes what it found; separate in/out buffers check binding order
	private const string Looking = """
		[[vk::binding(1,0)]] RWStructuredBuffer<uint> g_out;
		[[vk::binding(0,1)]] Sampler2D g_picture;

		[shader("compute")]
		[numthreads(1, 1, 1)]
		void main( uint3 id : SV_DispatchThreadID )
		{
			if ( id.x != 0 ) return;

			// The middle of it, as eight bits a channel.
			float4 seen = g_picture.SampleLevel( float2( 0.5, 0.5 ), 0.0 );

			g_out[0] = uint( seen.r * 255.0 + 0.5 );
			g_out[1] = uint( seen.g * 255.0 + 0.5 );
			g_out[2] = uint( seen.b * 255.0 + 0.5 );
		}
		""";

	private const string Source = """
		[[vk::binding(0,0)]] StructuredBuffer<uint> g_in;
		[[vk::binding(1,0)]] RWStructuredBuffer<uint> g_out;

		[[vk::push_constant]]
		cbuffer Push
		{
			uint g_add;
			uint g_count;
		};

		[shader("compute")]
		[numthreads(64, 1, 1)]
		void main( uint3 id : SV_DispatchThreadID )
		{
			if ( id.x >= g_count ) return;

			uint value = g_in[id.x];

			g_out[id.x] = ( value * value ) + g_add;
		}
		""";

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	//A .shader file with only MainCs compiles and runs, reading a buffer written after creation
	[TestMethod]
	public void AShaderFileMayBeNothingButAComputePass()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-compute-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		var pipeline = 0ul;
		var buffer = 0ul;
		var view = 0ul;

		try
		{
			var path = Path.Combine( directory, "mesher.shader" );

			//Its own numbers, the way any shader declares them, so the block the engine writes has to reach a compute stage as well as a drawing one.
			File.WriteAllText( path, """
				PARAMETERS
				{
					float g_flStride < Default( 3 ); Range( 1, 16 ); UiGroup( "Test" ); >;
				}

				[[vk::binding(0,0)]] RWStructuredBuffer<uint> g_out;

				[shader("compute")]
				[numthreads(64, 1, 1)]
				void MainCs( uint3 id : SV_DispatchThreadID )
				{
					if ( id.x >= 64 ) return;

					g_out[id.x] = id.x * uint( g_flStride );
				}
				""" );

			var stages = CoreShaderFile.Compile( path );

			Assert.IsNotNull( stages, $"a compute-only shader is still a shader: {CoreRenderDevice.ShaderError}" );

			var code = stages[(int)CoreShaderFile.Stage.Compute];

			Assert.IsNotNull( code, "the compute stage is what this file is" );
			Assert.IsNull( stages[(int)CoreShaderFile.Stage.Vertex],
				"it draws nothing, so there is no vertex stage to have compiled" );

			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			pipeline = CoreRenderDevice.CreateCompute( code );

			Assert.AreNotEqual( 0ul, pipeline, $"it would not build: {CoreRenderDevice.ShaderError}" );

			const int count = 64;

			buffer = CoreRenderDevice.CreateBuffer( new byte[count * 4] );

			Assert.AreNotEqual( 0ul, buffer );

			//Its numbers, packed the way the engine packs them - a compute pass has no material to read them out of, so the same block arrives in the push constants
			//instead. Packed by the same routine that lays out a material's, which is the whole point: the shader cannot tell which of the two it was handed.
			var numbers = new float[CoreShaderParams.PushSlots * 4];

			CoreShaderParams.Into( numbers, CoreShaderParams.Of( path ), _ => null );

			var push = System.Runtime.InteropServices.MemoryMarshal.AsBytes( numbers.AsSpan() );

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

			var dispatched = CoreRenderDevice.Dispatch( pipeline, new[] { buffer }, 1, push: push );

			CoreRenderDevice.EndViewFrame( view );

			Assert.IsTrue( dispatched, $"the dispatch should record: {CoreRenderDevice.Description}" );

			var back = new byte[count * 4];

			Assert.IsTrue( CoreRenderDevice.ReadBuffer( buffer, back ) );

			//The declared number never reached a compute stage before, because there was no compute stage
			//to reach: a stride of three is the block the engine wrote for it being read by the pass.
			for ( var i = 0; i < count; i++ )
			{
				Assert.AreEqual( (uint)( i * 3 ), BitConverter.ToUInt32( back, i * 4 ), $"thread {i}" );
			}
		}
		finally
		{
			if ( buffer != 0 ) CoreRenderDevice.DestroyBuffer( buffer );
			if ( pipeline != 0 ) CoreRenderDevice.DestroyPipeline( pipeline );
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );

			CoreRenderDevice.Shutdown();

			try { Directory.Delete( directory, true ); } catch { }
		}
	}

	[TestMethod]
	public void ABufferWrittenAfterItWasMadeIsWhatThePassReads()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		var spirv = CoreRenderDevice.CompileShaderSource( Source, null, "main", CoreShaderCompiler.Stage.Compute );

		Assert.IsNotNull( spirv, $"the compute shader would not compile: {CoreRenderDevice.ShaderError}" );

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		const int count = 64;

		var view = 0ul;
		var pipeline = 0ul;
		var input = 0ul;
		var output = 0ul;

		try
		{
			view = CoreRenderDevice.CreateView( 64, 64 );
			pipeline = CoreRenderDevice.CreateCompute( spirv );

			Assert.AreNotEqual( 0ul, view );
			Assert.AreNotEqual( 0ul, pipeline );

			//Made with ones, then written with the numbers that are meant to come out squared. If the write were lost
			//the answers would be one apiece, and if it arrived too late they would be whatever the pass happened to see.
			var ones = new uint[count];

			for ( var i = 0; i < count; i++ ) ones[i] = 1u;

			input = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( ones.AsSpan() ) );

			output = CoreRenderDevice.CreateBuffer( new byte[count * 4] );

			Assert.AreNotEqual( 0ul, input );
			Assert.AreNotEqual( 0ul, output );

			var numbers = new uint[count];

			for ( var i = 0; i < count; i++ ) numbers[i] = (uint)( i + 2 );

			//Half at a time, at an offset, because an offset that is ignored writes the
			//right numbers in the wrong place and a whole-buffer write would never notice.
			var half = count / 2;

			Assert.IsTrue( CoreRenderDevice.WriteBuffer( input, 0,
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( numbers.AsSpan( 0, half ) ) ),
				$"the first half should write: {CoreRenderDevice.Description}" );

			Assert.IsTrue( CoreRenderDevice.WriteBuffer( input, half * 4,
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( numbers.AsSpan( half ) ) ),
				$"the second half should write: {CoreRenderDevice.Description}" );

			var push = new byte[8];

			BitConverter.TryWriteBytes( push.AsSpan( 0 ), 0u );
			BitConverter.TryWriteBytes( push.AsSpan( 4 ), (uint)count );

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

			var dispatched = CoreRenderDevice.Dispatch( pipeline, new[] { input, output },
				( count + 63 ) / 64, push: push );

			CoreRenderDevice.EndViewFrame( view );

			Assert.IsTrue( dispatched, $"the dispatch should record: {CoreRenderDevice.Description}" );

			var back = new byte[count * 4];

			Assert.IsTrue( CoreRenderDevice.ReadBuffer( output, back ) );

			for ( var i = 0; i < count; i++ )
			{
				Assert.AreEqual( numbers[i] * numbers[i], BitConverter.ToUInt32( back, i * 4 ),
					$"element {i} - the pass read what the buffer was made with, not what was written" );
			}

			//And a write that runs off the end is refused rather than trusted.
			Assert.IsFalse( CoreRenderDevice.WriteBuffer( input, ( count - 1 ) * 4,
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( numbers.AsSpan() ) ),
				"a write past the end of a buffer should be refused" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyBuffer( input );
			CoreRenderDevice.DestroyBuffer( output );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	[TestMethod]
	public void ItRunsOverABufferAndTheAnswerComesBack()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		var spirv = CoreRenderDevice.CompileShaderSource( Source, null, "main", CoreShaderCompiler.Stage.Compute );

		Assert.IsNotNull( spirv, $"the compute shader would not compile: {CoreRenderDevice.ShaderError}" );
		Assert.IsTrue( spirv.Length > 0 );

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		const int count = 256;

		var view = 0ul;
		var pipeline = 0ul;
		var input = 0ul;
		var output = 0ul;

		try
		{
			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view );

			pipeline = CoreRenderDevice.CreateCompute( spirv );
			Assert.AreNotEqual( 0ul, pipeline, $"the compute pipeline should bake: {CoreRenderDevice.Description}" );

			var numbers = new uint[count];

			for ( var i = 0; i < count; i++ ) numbers[i] = (uint)( i + 1 );

			input = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( numbers.AsSpan() ) );

			//Zeroed, so that what comes back is what the pass wrote rather than what was there.
			output = CoreRenderDevice.CreateBuffer( new byte[count * 4] );

			Assert.AreNotEqual( 0ul, input );
			Assert.AreNotEqual( 0ul, output );

			var push = new byte[8];

			BitConverter.TryWriteBytes( push.AsSpan( 0 ), 7u );
			BitConverter.TryWriteBytes( push.AsSpan( 4 ), (uint)count );

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ),
				$"the frame would not begin: {CoreRenderDevice.Description}" );

			var buffers = new[] { input, output };

			var dispatched = CoreRenderDevice.Dispatch( pipeline, buffers, ( count + 63 ) / 64, push: push );

			CoreRenderDevice.EndViewFrame( view );

			Assert.IsTrue( dispatched, $"the dispatch should record: {CoreRenderDevice.Description}" );

			var back = new byte[count * 4];

			Assert.IsTrue( CoreRenderDevice.ReadBuffer( output, back ),
				$"the buffer should read back: {CoreRenderDevice.Description}" );

			for ( var i = 0; i < count; i++ )
			{
				var value = BitConverter.ToUInt32( back, i * 4 );
				var wanted = ( numbers[i] * numbers[i] ) + 7u;

				Assert.AreEqual( wanted, value, $"element {i}" );
			}

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyBuffer( input );
			CoreRenderDevice.DestroyBuffer( output );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//A dispatch between draws must close and reopen the render pass without validation errors
	[TestMethod]
	public void ItCanRunBetweenDraws()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		var spirv = CoreRenderDevice.CompileShaderSource( Source, null, "main", CoreShaderCompiler.Stage.Compute );
		if ( spirv is null ) { Assert.Inconclusive( "the compute shader would not compile" ); return; }

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var view = 0ul;
		var pipeline = 0ul;
		var buffer = 0ul;

		try
		{
			var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "no box or no pipelines" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var box = scene.CreateObject();
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -3, 0, 0 );
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();
			camera.BackgroundColor = new Color( 0.0f, 0.0f, 1.0f );
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;

			view = CoreRenderDevice.CreateView( 64, 64 );
			pipeline = CoreRenderDevice.CreateCompute( spirv );
			buffer = CoreRenderDevice.CreateBuffer( new byte[64 * 4] );

			Assert.AreNotEqual( 0ul, view );
			Assert.AreNotEqual( 0ul, pipeline );
			Assert.AreNotEqual( 0ul, buffer );

			var drawer = new CoreSceneDrawer();
			var pixels = new byte[64 * 64 * 4];
			var errorsBefore = CoreErrors.Count;

			var push = new byte[8];
			BitConverter.TryWriteBytes( push.AsSpan( 4 ), 64u );

			//Alternate dispatching before and after the scene draws, one frame each
			for ( var frame = 0; frame < 4; frame++ )
			{
				if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) continue;

				var first = ( frame % 2 ) == 0;

				try
				{
					if ( first )
					{
						Assert.IsTrue( CoreRenderDevice.Dispatch( pipeline, new[] { buffer }, 1, push: push ),
							"the dispatch should record before anything has drawn" );
					}

					drawer.Draw( scene, camera, view, 64, 64 );

					if ( !first )
					{
						Assert.IsTrue( CoreRenderDevice.Dispatch( pipeline, new[] { buffer }, 1, push: push ),
							"the dispatch should record after a frame has drawn" );
					}
				}
				finally
				{
					CoreRenderDevice.EndViewFrame( view );
				}
			}

			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			var middle = ( ( 32 * 64 ) + 32 ) * 4;

			var background = pixels[middle + 2] > 200 && pixels[middle] < 40 && pixels[middle + 1] < 40;

			Assert.IsFalse( background, "the box was not drawn in a frame that also ran a compute pass" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyBuffer( buffer );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//A pass can read a picture, not only a buffer.
	[TestMethod]
	public void APassCanLookAtAPicture()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "rintencore is not loaded" ); return; }

		if ( !CoreShaderCompiler.Available )
		{
			Assert.Inconclusive( "no shader compiler on this host" );
			return;
		}

		var spirv = CoreRenderDevice.CompileShaderSource( Looking, null, "main",
			CoreShaderCompiler.Stage.Compute );

		Assert.IsNotNull( spirv, $"the shader would not compile: {CoreRenderDevice.ShaderError}" );

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var view = 0ul;
		var pipeline = 0ul;
		var picture = 0ul;
		var buffer = 0ul;

		try
		{
			view = CoreRenderDevice.CreateView( 16, 16 );
			Assert.AreNotEqual( 0ul, view );

			pipeline = CoreRenderDevice.CreateCompute( spirv );
			Assert.AreNotEqual( 0ul, pipeline, $"the pipeline should bake: {CoreRenderDevice.Description}" );

			//One colour, so that whichever texel it lands on says the same thing.
			var pixels = new byte[4 * 4 * 4];

			for ( var i = 0; i < pixels.Length; i += 4 )
			{
				pixels[i] = 40;
				pixels[i + 1] = 160;
				pixels[i + 2] = 200;
				pixels[i + 3] = 255;
			}

			picture = CoreRenderDevice.CreateTextureRgba( pixels, 4, 4, srgb: false );
			Assert.AreNotEqual( 0ul, picture );

			buffer = CoreRenderDevice.CreateBuffer( new byte[16] );
			Assert.AreNotEqual( 0ul, buffer );

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

			try
			{
				Assert.IsTrue( CoreRenderDevice.Dispatch( pipeline, new[] { buffer }, 1, texture: picture ),
					$"the pass should record: {CoreRenderDevice.Description}" );
			}
			finally
			{
				CoreRenderDevice.EndViewFrame( view );
			}

			var read = new byte[16];

			Assert.IsTrue( CoreRenderDevice.ReadBuffer( buffer, read ) );

			Assert.AreEqual( 40u, BitConverter.ToUInt32( read, 0 ), "the red it read" );
			Assert.AreEqual( 160u, BitConverter.ToUInt32( read, 4 ), "the green it read" );
			Assert.AreEqual( 200u, BitConverter.ToUInt32( read, 8 ), "the blue it read" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyBuffer( buffer );
			CoreRenderDevice.DestroyTexture( picture );
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
