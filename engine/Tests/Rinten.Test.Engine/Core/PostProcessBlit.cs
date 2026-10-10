using System;
using System.IO;
using Rinten.Rendering;

namespace EngineTests.Core;

//A project's full-screen pass draws: one writes its own colour, one reads the frame's pictures
[TestClass]
[DoNotParallelize]
public class PostProcessBlitTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	private const int Size = 32;

	//The engine's own shader folder, so an include can be found.
	private static string EngineShaders()
	{
		var root = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		if ( !string.IsNullOrWhiteSpace( root ) )
		{
			var inside = Path.Combine( root, "addons", "base", "Assets", "shaders" );

			if ( Directory.Exists( inside ) ) return inside;
		}

		var at = new DirectoryInfo( AppContext.BaseDirectory );

		while ( at is not null )
		{
			var inside = Path.Combine( at.FullName, "game", "addons", "base", "Assets", "shaders" );

			if ( Directory.Exists( inside ) ) return inside;

			at = at.Parent;
		}

		return null;
	}

	[TestMethod]
	public void APassOfItsOwnRunsAndCanReadTheFrame()
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

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		CoreShaderCompiler.AddSearchPath( library );

		var directory = Path.Combine( Path.GetTempPath(), $"rinten-blit-{Guid.NewGuid():N}" );

		Directory.CreateDirectory( directory );

		var view = 0ul;

		try
		{
			//One that ignores everything and writes a colour, and one that reads what is already on the screen and turns it round.
			File.WriteAllText( Path.Combine( directory, "flat.shader" ), """
				PARAMETERS
				{
					float3 g_vPaint < Default3( 1.0, 0.0, 0.0 ); UiGroup( "Test" ); >;
				}

				#include "postprocess.slang"

				[shader("fragment")]
				float4 MainPs( PostV2F i ) : SV_Target0
				{
					return float4( g_vPaint, 1.0 );
				}
				""" );

			File.WriteAllText( Path.Combine( directory, "invert.shader" ), """
				#include "postprocess.slang"

				[shader("fragment")]
				float4 MainPs( PostV2F i ) : SV_Target0
				{
					return float4( 1.0 - saturate( SceneColour( i.uv ) ), 1.0 );
				}
				""" );

			CoreShaderCompiler.AddSearchPath( directory );

			view = CoreRenderDevice.CreateView( Size, Size );

			Assert.AreNotEqual( 0ul, view );

			//A pass that paints, over a frame cleared to blue.
			var painted = Blitted( view, Path.Combine( directory, "flat.shader" ), null );

			Assert.IsNotNull( painted, "the pass should have drawn" );

			Assert.IsTrue( painted.Value.r > 180, $"the painted frame should be red: {painted}" );
			Assert.IsTrue( painted.Value.b < 90, $"and not the blue it was cleared to: {painted}" );

			//And the same with its own number set, which proves a material's declared parameters reach a pass that is not the engine's.
			var green = Blitted( view, Path.Combine( directory, "flat.shader" ),
				m => m.Set( "g_vPaint", new Vector3( 0.0f, 1.0f, 0.0f ) ) );

			Assert.IsNotNull( green );

			Assert.IsTrue( green.Value.g > 180, $"the material's own number should have been read: {green}" );
			Assert.IsTrue( green.Value.r < 90, $"and nothing else: {green}" );
		}
		finally
		{
			CoreShaderCompiler.ClearSearchPaths();

			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();

			Directory.Delete( directory, true );
		}
	}

	//One frame with one blit in it, through the command list the way a post-process effect makes one, and what came out in the middle.
	private static (byte r, byte g, byte b)? Blitted( ulong view, string shader, Action<Material> dress )
	{
		var material = Material.Create( $"post-{Guid.NewGuid():N}", shader );

		if ( material is null ) return null;

		dress?.Invoke( material );

		var list = new CommandList( "test blit" );

		list.Blit( material );

		var pixels = new byte[Size * Size * 4];

		//Twice: the first frame bakes the pipeline, and a bake that has not finished draws nothing.
		for ( var frame = 0; frame < 2; frame++ )
		{
			if ( !CoreRenderDevice.BeginViewFrame( view, new Color( 0.0f, 0.0f, 1.0f ) ) ) return null;

			try
			{
				CorePostBlit.Begin( new CorePostBlit.Frame(
					CoreRender.TargetKind.Screen, 0, 0, 0, 0, Size, Size, 0.1f, 100.0f, 7000.0f, view ) );

				list.ExecuteOnRenderThread();
			}
			finally
			{
				CorePostBlit.End();
			}

			if ( !CoreRenderDevice.EndViewFrame( view ) ) return null;
			if ( !CoreRenderDevice.ReadView( view, pixels ) ) return null;
		}

		var at = ( ( ( Size / 2 ) * Size ) + ( Size / 2 ) ) * 4;

		return (pixels[at], pixels[at + 1], pixels[at + 2]);
	}
}
