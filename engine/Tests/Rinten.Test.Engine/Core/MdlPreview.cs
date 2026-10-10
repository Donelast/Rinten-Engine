using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//Opening a skinned model in the editor uploads widget pictures during a view's recording. That is what killed the device when a skinned model opened.
[TestClass]
[DoNotParallelize]
public class MdlPreviewTests
{
	[TestInitialize]
	public void OnMainThread()
	{
		ThreadSafe.MarkMainThread();
	}

	[TestMethod]
	public void UploadsAWidgetSizedPicture()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			var width = 370;
			var height = 802;
			var pixels = new byte[width * height * 4];
			Array.Fill( pixels, (byte)255 );

			var errorsBefore = CoreErrors.Count;

			for ( var i = 0; i < 8; i++ )
			{
				var handle = CoreRenderDevice.CreateTextureRgba( pixels, width, height, srgb: false );
				Assert.AreNotEqual( 0ul, handle,
					$"upload {i} of a {width}x{height} picture failed: {CoreRenderDevice.Description}" );
				CoreRenderDevice.DestroyTexture( handle );
			}

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.Shutdown();
		}
	}

	[TestMethod]
	public void UploadsWhileAViewIsRecording()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var view = 0ul;
		var handle = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view, "a view needs no window" );

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, new Color( 0.1f, 0.1f, 0.13f ) ),
				$"the view would not begin: {CoreRenderDevice.Description}" );

			var pixels = new byte[370 * 802 * 4];
			handle = CoreRenderDevice.CreateTextureRgba( pixels, 370, 802, srgb: false );

			Assert.AreNotEqual( 0ul, handle,
				$"a picture uploaded during a view failed: {CoreRenderDevice.Description}" );

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ),
				$"the view would not end: {CoreRenderDevice.Description}" );
		}
		finally
		{
			CoreRenderDevice.DestroyTexture( handle );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	[TestMethod]
	public unsafe void DrawsWithUnusedHighBoneIndices()
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

		var modelPath = Path.Combine( Path.GetTempPath(), $"rinten-skin-high-{Guid.NewGuid():N}.mdl_c" );
		CoreRenderDemo.BuildCubeMesh( out var vertices, out var indices );

		var boneIndices = new byte[vertices.Length * 4];
		var boneWeights = new byte[vertices.Length * 4];

		for ( var i = 0; i < vertices.Length; i++ )
		{
			boneIndices[i * 4] = 0;
			boneIndices[( i * 4 ) + 1] = 255;
			boneIndices[( i * 4 ) + 2] = 255;
			boneIndices[( i * 4 ) + 3] = 255;
			boneWeights[i * 4] = 255;
		}

		var builder = new CoreAssets.ModelBuilder();

		fixed ( Vertex* v = vertices ) builder.AddVertices( v, vertices.Length );
		fixed ( uint* i = indices ) builder.AddIndices( i, indices.Length );

		fixed ( byte* b = boneIndices )
		fixed ( byte* w = boneWeights )
		{
			Assert.AreEqual( 0, builder.AddSkin( b, w, vertices.Length ) );
		}

		var identity = stackalloc float[16];
		for ( var i = 0; i < 16; i++ ) identity[i] = ( i % 5 ) == 0 ? 1.0f : 0.0f;

		Assert.AreEqual( 0, builder.AddBone( "root", -1, identity ) );
		builder.AddMesh( "cube", "", 0, indices.Length, 0, vertices.Length );

		Assert.AreEqual( 1, builder.Save( modelPath ) );
		builder.Dispose();

		var view = 0ul;
		var pipeline = 0ul;
		var fallback = 0ul;
		CoreSceneRenderer.SceneMesh mesh = null;
		var meshes = new Dictionary<string, CoreSceneRenderer.SceneMesh>();
		var textures = new Dictionary<string, ulong>();

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view );

			pipeline = CoreSceneRenderer.MakeSkinnedPipeline();
			Assert.AreNotEqual( 0ul, pipeline, $"the skinning pipeline should bake: {CoreRenderDevice.Description}" );

			fallback = CoreRenderDemo.MakeCheckerTexture();
			mesh = CoreSceneRenderer.LoadMesh( modelPath, meshes, textures, fallback );
			Assert.IsNotNull( mesh );
			Assert.IsTrue( mesh.IsSkinned );

			var pose = new System.Numerics.Matrix4x4[] { System.Numerics.Matrix4x4.Identity };
			var look = System.Numerics.Matrix4x4.CreateLookAt(
				new System.Numerics.Vector3( 0, 0, 2.5f ),
				System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY );
			var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView( 1.0f, 1.0f, 0.05f, 20.0f );
			projection.M22 = -projection.M22;

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, new Color( 0.1f, 0.1f, 0.13f ) ) );
			CoreRenderDevice.SetupFrame( CoreSceneRenderer.BuildFrame( null, look * projection, Vector3.Zero ) );

			var push = CoreSceneRenderer.ScenePush( System.Numerics.Matrix4x4.Identity, Color.White );
			var drawn = 0;

			foreach ( var part in mesh.Meshes )
			{
				if ( CoreRenderDevice.DrawSkinned( pipeline, mesh.Vertices, mesh.Skin, mesh.Indices,
					part.IndexCount, pose, push, part.FirstIndex, part.FirstVertex, part.Texture ) )
					drawn++;
			}

			Assert.IsTrue( drawn > 0, "a mesh with unused links at 255 should still record" );
			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ),
				$"unused links at 255 lost the device: {CoreRenderDevice.Description}" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyPipeline( pipeline );

			if ( mesh is not null )
			{
				CoreRenderDevice.DestroyBuffer( mesh.Vertices );
				CoreRenderDevice.DestroyBuffer( mesh.Indices );
				CoreRenderDevice.DestroyBuffer( mesh.Skin );
			}

			foreach ( var texture in textures.Values )
				CoreRenderDevice.DestroyTexture( texture );

			CoreRenderDevice.DestroyTexture( fallback );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			try { File.Delete( modelPath ); } catch ( IOException ) { }
		}
	}

	[TestMethod]
	public unsafe void AViewSurvivesTheOcclusionSampleTheEditorUses()
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

		var view = 0ul;
		var pipeline = 0ul;
		var vertices = 0ul;
		var indices = 0ul;
		var fallback = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize( validation: true ) )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			view = CoreRenderDevice.CreateView( 64, 64 );
			Assert.AreNotEqual( 0ul, view, "a view needs no window" );

			pipeline = CoreSceneRenderer.MakeScenePipeline();
			Assert.AreNotEqual( 0ul, pipeline, $"standard shading should bake: {CoreRenderDevice.Description}" );

			CoreRenderDemo.BuildCubeMesh( out var mesh, out var indexList );
			vertices = CoreRenderDevice.CreateBuffer( MemoryMarshal.AsBytes( mesh.AsSpan() ) );
			indices = CoreRenderDevice.CreateBuffer( MemoryMarshal.AsBytes( indexList.AsSpan() ) );
			fallback = CoreRenderDemo.MakeCheckerTexture();

			Assert.AreNotEqual( 0ul, vertices );
			Assert.AreNotEqual( 0ul, indices );

			var look = System.Numerics.Matrix4x4.CreateLookAt(
				new System.Numerics.Vector3( 0, 0, 2.5f ),
				System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY );
			var projection = System.Numerics.Matrix4x4.CreatePerspectiveFieldOfView( 1.0f, 1.0f, 0.05f, 20.0f );
			projection.M22 = -projection.M22;

			var frame = CoreSceneRenderer.BuildFrame( null, look * projection, Vector3.Zero );
			CoreSceneRenderer.SetOcclusion( frame, 64, 64 );

			var errorsBefore = CoreErrors.Count;

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, new Color( 0.1f, 0.1f, 0.13f ) ),
				$"the view would not begin: {CoreRenderDevice.Description}" );

			CoreRenderDevice.SetupFrame( frame );

			var push = CoreSceneRenderer.ScenePush( System.Numerics.Matrix4x4.Identity, Color.White );
			Assert.IsTrue( CoreRenderDevice.DrawIndexed( pipeline, vertices, indices,
				indexList.Length, push, texture: fallback ),
				"a shaded cube should record" );

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ),
				$"sampling occlusion lost the device: {CoreRenderDevice.Description}" );

			if ( CoreRenderDevice.ValidationEnabled )
			{
				Assert.AreEqual( errorsBefore, CoreErrors.Count,
					"validation reported errors - they are in the test log, by name" );
			}
		}
		finally
		{
			CoreRenderDevice.DestroyPipeline( pipeline );
			CoreRenderDevice.DestroyBuffer( vertices );
			CoreRenderDevice.DestroyBuffer( indices );
			CoreRenderDevice.DestroyTexture( fallback );
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
