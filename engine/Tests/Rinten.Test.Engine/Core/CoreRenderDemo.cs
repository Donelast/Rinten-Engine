using System;
using System.Diagnostics;
using System.IO;

using static Rinten.Internal.GlobalGameNamespace;

using CoreAssets = Rinten.CoreAssets;

namespace Rinten;

//Debug command that opens a window drawing through our renderer, to check the stack end to end
internal static class CoreRenderDemo
{
	//The first shader. Geometry from the vertex index - no buffers exist yet - and a colour per corner so the interpolation is visible too.
	private const string TriangleShader = """
		struct V2F
		{
			float4 position : SV_Position;
			float3 colour : COLOR0;
		};

		static const float2 positions[3] = { float2( 0.0, -0.6 ), float2( 0.6, 0.6 ), float2( -0.6, 0.6 ) };
		static const float3 colours[3] = { float3( 1, 0.2, 0.2 ), float3( 0.2, 1, 0.2 ), float3( 0.2, 0.4, 1 ) };

		[shader("vertex")]
		V2F MainVs( uint id : SV_VertexID )
		{
			V2F o;
			o.position = float4( positions[id], 0.0, 1.0 );
			o.colour = colours[id];
			return o;
		}

		[shader("fragment")]
		float4 MainPs( V2F i ) : SV_Target
		{
			return float4( i.colour, 1.0 );
		}
		""";

	//The second shader: real vertices, in the engine's own layout, seen through a camera
	//that arrives as push constants. The locations are the fixed ones of SBC_VERTEX_ENGINE.
	private const string CubeShader = """
		struct VIn
		{
			[[vk::location(0)]] float3 position : POSITION;
			[[vk::location(2)]] float3 normal : NORMAL;
			[[vk::location(3)]] float4 texCoord0 : TEXCOORD0;
		};

		struct V2F
		{
			float4 position : SV_Position;
			float3 normal : NORMAL;
			float2 uv : TEXCOORD0;
		};

		[[vk::push_constant]]
		cbuffer Push
		{
			row_major float4x4 g_worldToClip;
			float3 g_lightInModel;
		};

		[[vk::binding(0,0)]] Sampler2D g_color;

		[shader("vertex")]
		V2F MainVs( VIn i )
		{
			V2F o;
			o.position = mul( float4( i.position, 1.0 ), g_worldToClip );
			o.normal = i.normal;
			o.uv = i.texCoord0.xy;
			return o;
		}

		[shader("fragment")]
		float4 MainPs( V2F i ) : SV_Target
		{
			float d = saturate( dot( normalize( i.normal ), -normalize( g_lightInModel ) ) ) * 0.75 + 0.25;
			return float4( g_color.Sample( i.uv ).rgb * d, 1.0 );
		}
		""";

	/// <summary>Opens a window and draws into it for a few seconds, blocking this thread.</summary>
	[ConCmd( "core_render_window" )]
	internal static void Run( float seconds = 5.0f, string modelPath = null, string texturePath = null )
	{
		if ( !CoreNative.Available )
		{
			Log.Warning( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		using var window = CoreWindow.Create( EngineExecutables.Editor, 1280, 720 );

		if ( window is null )
		{
			Log.Warning( $"could not open a window: {CoreWindow.LastError}" );
			return;
		}

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Log.Warning( $"no graphics device: {CoreRenderDevice.Description}" );
			return;
		}

		var swapchain = CoreRenderDevice.AttachWindow( window );

		if ( swapchain == 0 )
		{
			Log.Warning( $"no swapchain: {CoreRenderDevice.Description}" );
			CoreRenderDevice.Shutdown();
			return;
		}

		//A model from disk if one was named, the built-in cube otherwise. Either way it is the same
		//buffers, the same layout and the same shader - a model is only a mesh somebody else made.
		var pipeline = string.IsNullOrEmpty( modelPath ) ? MakeTrianglePipeline() : 0;
		var cube = string.IsNullOrEmpty( modelPath ) ? MakeCube() : MakeModelMesh( modelPath );

		//A texture from disk if one was named, the built-in checker otherwise.
		cube.Texture = string.IsNullOrEmpty( texturePath )
			? MakeCheckerTexture()
			: CoreRenderDevice.CreateTexture( texturePath );

		window.Show();

		var clock = Stopwatch.StartNew();
		var lastSize = window.SizeInPixels;
		var errorsBefore = CoreErrors.Count;

		//Three placements of the same mesh; stackalloc kept outside the loop (CA2014)
		Span<System.Numerics.Vector3> stands = stackalloc System.Numerics.Vector3[]
		{
			default,
			new( -cube.Radius * 1.6f, cube.Radius * 0.4f, -cube.Radius * 1.2f ),
			new( cube.Radius * 1.6f, -cube.Radius * 0.4f, -cube.Radius * 1.2f ),
		};

		while ( clock.Elapsed.TotalSeconds < seconds )
		{
			CoreWindow.Pump();

			//Nothing here is listening for a resize event, so notice it the plain way. A real loop will be told rather than having to look.
			var size = window.SizeInPixels;

			if ( size != lastSize && size.Width > 0 && size.Height > 0 )
			{
				CoreRenderDevice.Resize( swapchain, size.Width, size.Height );
				lastSize = size;
			}

			//A background that is obviously alive, so a frozen window is not mistaken for a working one.
			var t = (float)clock.Elapsed.TotalSeconds;
			var background = new Color(
				0.10f + 0.05f * MathF.Sin( t * 1.1f ),
				0.12f + 0.05f * MathF.Sin( t * 1.7f + 2.0f ),
				0.16f + 0.05f * MathF.Sin( t * 2.3f + 4.0f ) );

			if ( !CoreRenderDevice.BeginFrame( swapchain, background ) ) continue;

			//The triangle first, without depth, then the cube over it with depth - two pipelines with different states sharing one frame, on purpose.
			if ( pipeline != 0 ) CoreRenderDevice.Draw( pipeline, 3 );

			if ( cube.Pipeline != 0 )
			{
				var aspect = lastSize.Height > 0 ? lastSize.Width / (float)lastSize.Height : 1.0f;

				for ( var i = 0; i < stands.Length; i++ )
				{
					var push = CubePush( t + i * 0.7f, aspect, cube.Center, cube.Radius, stands[i] );

					CoreRenderDevice.DrawIndexed( cube.Pipeline, cube.Vertices, cube.Indices,
						cube.IndexCount, push, texture: cube.Texture );
				}
			}

			CoreRenderDevice.EndFrame();
		}

		var errors = CoreErrors.Count - errorsBefore;

		Log.Info( $"{CoreRenderDevice.FrameCount( swapchain )} frames in {clock.Elapsed.TotalSeconds:0.0}s - {CoreRenderDevice.Description}" );

		if ( CoreRenderDevice.ValidationEnabled )
			Log.Info( errors == 0 ? "validation: clean" : $"validation: {errors} errors - see the log above" );
		else
			Log.Info( "validation: not present on this machine (install the Vulkan SDK to get it)" );

		CoreRenderDevice.DestroyPipeline( pipeline );
		CoreRenderDevice.DestroyPipeline( cube.Pipeline );
		CoreRenderDevice.DestroyBuffer( cube.Vertices );
		CoreRenderDevice.DestroyBuffer( cube.Indices );
		CoreRenderDevice.DestroyTexture( cube.Texture );
		CoreRenderDevice.DetachWindow( swapchain );
		CoreRenderDevice.Shutdown();
	}

	internal record struct CubeParts( ulong Pipeline, ulong Vertices, ulong Indices, int IndexCount,
		System.Numerics.Vector3 Center, float Radius )
	{
		public ulong Texture;
	}

	//An orange-and-cream checker, made right here. What it proves is UVs and sampling, which is
	//exactly what it shows: a wrong layout scrambles the squares, a missing flip turns them over.
	internal static ulong MakeCheckerTexture()
	{
		const int size = 256;
		const int cell = 32;

		var pixels = new byte[size * size * 4];

		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				var dark = ( ( x / cell ) + ( y / cell ) ) % 2 == 0;
				var at = ( y * size + x ) * 4;

				pixels[at + 0] = dark ? (byte)214 : (byte)244;
				pixels[at + 1] = dark ? (byte)120 : (byte)238;
				pixels[at + 2] = dark ? (byte)40 : (byte)224;
				pixels[at + 3] = 255;
			}
		}

		return CoreRenderDevice.CreateTextureRgba( pixels, size, size, srgb: true );
	}

	//A unit cube in the engine vertex layout, its pipeline, and its buffers.
	internal static CubeParts MakeCube()
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-cube-{Guid.NewGuid():N}.slang" );

		try
		{
			File.WriteAllText( path, CubeShader );

			var vertex = CoreRenderDevice.CompileShader( path, "MainVs", CoreShaderCompiler.Stage.Vertex );
			var pixel = CoreRenderDevice.CompileShader( path, "MainPs", CoreShaderCompiler.Stage.Pixel );

			if ( vertex is null || pixel is null )
			{
				Log.Warning( $"no cube - the shader did not compile: {CoreRenderDevice.ShaderError}" );
				return default;
			}

			var pipeline = CoreRenderDevice.CreatePipeline( vertex, pixel,
				CoreRender.VertexLayoutKind.Engine, depthTest: true );

			if ( pipeline == 0 ) return default;

			BuildCubeMesh( out var vertices, out var indices );

			var vertexBuffer = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( vertices.AsSpan() ) );
			var indexBuffer = CoreRenderDevice.CreateBuffer(
				System.Runtime.InteropServices.MemoryMarshal.AsBytes( indices.AsSpan() ) );

			if ( vertexBuffer == 0 || indexBuffer == 0 )
			{
				CoreRenderDevice.DestroyPipeline( pipeline );
				CoreRenderDevice.DestroyBuffer( vertexBuffer );
				CoreRenderDevice.DestroyBuffer( indexBuffer );
				return default;
			}

			//Half the diagonal of a unit cube, so the camera maths is the same whether it is framing this or a model with real bounds.
			return new CubeParts( pipeline, vertexBuffer, indexBuffer, indices.Length,
				System.Numerics.Vector3.Zero, 0.87f );
		}
		finally
		{
			File.Delete( path );
		}
	}

	//A compiled model from disk - .mdl_c - in the same buffers, same layout and same shader as the cube, with the camera framed on its bounds.
	internal static unsafe CubeParts MakeModelMesh( string path )
	{
		var model = CoreAssets.ModelFile.Load( path );

		if ( model is not { Valid: true } )
		{
			Log.Warning( $"the model would not load: {path}" );
			return default;
		}

		try
		{
			var vertexCount = model.VertexCount;
			var indexCount = model.IndexCount;

			if ( vertexCount <= 0 || indexCount <= 0 )
			{
				Log.Warning( $"the model has no geometry: {path}" );
				return default;
			}

			var vertices = new Vertex[vertexCount];
			var indices = new uint[indexCount];

			fixed ( Vertex* v = vertices )
			{
				model.CopyVertices( v, vertexCount );
			}

			fixed ( uint* i = indices )
			{
				model.CopyIndices( i, indexCount );
			}

			var bounds = stackalloc float[6];
			model.GetRenderBounds( bounds );

			var min = new System.Numerics.Vector3( bounds[0], bounds[1], bounds[2] );
			var max = new System.Numerics.Vector3( bounds[3], bounds[4], bounds[5] );
			var center = ( min + max ) * 0.5f;
			var radius = MathF.Max( 0.01f, ( max - min ).Length() * 0.5f );

			var shaderPath = Path.Combine( Path.GetTempPath(), $"rinten-model-{Guid.NewGuid():N}.slang" );

			try
			{
				File.WriteAllText( shaderPath, CubeShader );

				var vs = CoreRenderDevice.CompileShader( shaderPath, "MainVs", CoreShaderCompiler.Stage.Vertex );
				var ps = CoreRenderDevice.CompileShader( shaderPath, "MainPs", CoreShaderCompiler.Stage.Pixel );

				if ( vs is null || ps is null )
				{
					Log.Warning( $"no shader for the model: {CoreRenderDevice.ShaderError}" );
					return default;
				}

				var pipeline = CoreRenderDevice.CreatePipeline( vs, ps,
					CoreRender.VertexLayoutKind.Engine, depthTest: true );

				if ( pipeline == 0 ) return default;

				var vertexBuffer = CoreRenderDevice.CreateBuffer(
					System.Runtime.InteropServices.MemoryMarshal.AsBytes( vertices.AsSpan() ) );
				var indexBuffer = CoreRenderDevice.CreateBuffer(
					System.Runtime.InteropServices.MemoryMarshal.AsBytes( indices.AsSpan() ) );

				if ( vertexBuffer == 0 || indexBuffer == 0 )
				{
					CoreRenderDevice.DestroyPipeline( pipeline );
					CoreRenderDevice.DestroyBuffer( vertexBuffer );
					CoreRenderDevice.DestroyBuffer( indexBuffer );
					return default;
				}

				Log.Info( $"model: {vertexCount} vertices, {indexCount} indices, radius {radius:0.##}m" );

				return new CubeParts( pipeline, vertexBuffer, indexBuffer, indexCount, center, radius );
			}
			finally
			{
				File.Delete( shaderPath );
			}
		}
		finally
		{
			model.Dispose();
		}
	}

	//Twenty-four vertices - four per face, so every face keeps its own flat normal
	//- in the engine's Vertex struct, which is byte-identical to what .mdl_c stores.
	internal static void BuildCubeMesh( out Vertex[] vertices, out uint[] indices )
	{
		Span<(Vector3 Normal, Vector3 Up)> faces = stackalloc (Vector3, Vector3)[]
		{
			( new Vector3( 1, 0, 0 ), new Vector3( 0, 1, 0 ) ),
			( new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ) ),
			( new Vector3( 0, 1, 0 ), new Vector3( 0, 0, 1 ) ),
			( new Vector3( 0, -1, 0 ), new Vector3( 0, 0, 1 ) ),
			( new Vector3( 0, 0, 1 ), new Vector3( 0, 1, 0 ) ),
			( new Vector3( 0, 0, -1 ), new Vector3( 0, 1, 0 ) ),
		};

		vertices = new Vertex[24];
		indices = new uint[36];

		for ( var face = 0; face < 6; face++ )
		{
			var (normal, up) = faces[face];

			//Right-handed: right cross up has to be the normal, or the corners below wind clockwise seen from outside and the cube is inside out.
			var right = Vector3.Cross( up, normal );

			for ( var corner = 0; corner < 4; corner++ )
			{
				var u = ( corner & 1 ) == 0 ? -1.0f : 1.0f;
				var v = ( corner & 2 ) == 0 ? -1.0f : 1.0f;

				var position = ( normal + right * u + up * v ) * 0.5f;

				vertices[face * 4 + corner] = new Vertex( position, normal, right, new Vector4( u, v, 0, 0 ) );
			}

			var start = (uint)( face * 4 );

			indices[face * 6 + 0] = start;
			indices[face * 6 + 1] = start + 1;
			indices[face * 6 + 2] = start + 2;
			indices[face * 6 + 3] = start + 2;
			indices[face * 6 + 4] = start + 1;
			indices[face * 6 + 5] = start + 3;
		}
	}

	//The camera and the light for one frame of the spinning cube, laid out exactly as the shader's push
	//constant block reads them: a row-major world-to-clip matrix, then the light direction in model space.
	internal static byte[] CubePush( float t, float aspect,
		System.Numerics.Vector3 center = default, float radius = 0.87f,
		System.Numerics.Vector3 offset = default )
	{
		//Spin about the mesh's own centre, however far from its origin the modeller left it; then out to wherever this copy of it stands.
		var model =
			System.Numerics.Matrix4x4.CreateTranslation( -center ) *
			System.Numerics.Matrix4x4.CreateFromYawPitchRoll( t * 0.9f, t * 0.55f, 0 ) *
			System.Numerics.Matrix4x4.CreateTranslation( offset );

		var view = System.Numerics.Matrix4x4.CreateLookAt(
			new System.Numerics.Vector3( 0, 0, radius * 2.6f ),
			System.Numerics.Vector3.Zero,
			System.Numerics.Vector3.UnitY );

		var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView(
			1.0f, aspect > 0 ? aspect : 1.0f, MathF.Max( 0.01f, radius * 0.05f ), radius * 20.0f );

		//Vulkan's clip space points Y down where Direct3D's - and therefore System.Numerics'
		//- points up. One sign flip here, or an upside-down world everywhere else.
		projection.M22 = -projection.M22;

		var worldToClip = model * view * projection;

		//The shader lights with the model-space normal, so the world-space light is carried into
		//model space with it - the transpose is the inverse, the model matrix being pure rotation.
		var light = System.Numerics.Vector3.TransformNormal(
			new System.Numerics.Vector3( 0.4f, -1.0f, -0.6f ),
			System.Numerics.Matrix4x4.Transpose( model ) );

		var push = new byte[80];
		var span = push.AsSpan();

		System.Runtime.InteropServices.MemoryMarshal.Write( span, in worldToClip );
		System.Runtime.InteropServices.MemoryMarshal.Write( span[64..], in light );

		return push;
	}

	//Slang source to SPIR-V to a baked pipeline, all right now. Zero if any step of that is not available, and the demo falls back to a bare clear.
	private static ulong MakeTrianglePipeline()
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-triangle-{Guid.NewGuid():N}.slang" );

		try
		{
			File.WriteAllText( path, TriangleShader );

			var vertex = CoreRenderDevice.CompileShader( path, "MainVs", CoreShaderCompiler.Stage.Vertex );
			var pixel = CoreRenderDevice.CompileShader( path, "MainPs", CoreShaderCompiler.Stage.Pixel );

			if ( vertex is null || pixel is null )
			{
				Log.Warning( $"no triangle - the shader did not compile: {CoreRenderDevice.ShaderError}" );
				return 0;
			}

			return CoreRenderDevice.CreatePipeline( vertex, pixel );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
