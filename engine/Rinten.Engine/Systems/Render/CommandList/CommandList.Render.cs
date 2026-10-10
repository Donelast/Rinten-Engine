namespace Rinten.Rendering;


public sealed unsafe partial class CommandList
{
	/// <summary>Render a <see cref="Renderer"/> with the specified overrides.</summary>
	public void DrawRenderer( Renderer renderer, RendererSetup rendererSetup = default )
	{
		if ( !renderer.IsValid() )
			return;

		static void Execute( ref Entry entry, CommandList commandList )
		{
			var drawer = CoreSceneDrawer.Drawing;

			if ( drawer is null ) return;
			if ( entry.Object1 is not Renderer renderer || !renderer.IsValid() ) return;

			//A draw lands on the frame, or reads it, so what is held goes on first.
			CorePostBlit.Flush();
			if ( entry.Object2 is not Material material || !material.IsValid() ) return;

			drawer.DrawRendererNow( renderer, material,
				entry.Data1.x > 0.5f, commandList.Values, entry.Data1.y > 0.5f );
		}

		AddEntry( &Execute, new Entry
		{
			Object1 = renderer,
			Object2 = rendererSetup.Material,
			Data1 = new Vector4( rendererSetup.IgnoreDepth ? 1.0f : 0.0f, rendererSetup.ReadsOwnVertices ? 1.0f : 0.0f, 0.0f, 0.0f ),
		} );
	}

	/// <summary>Renders the view from a camera to the specified render target.</summary>
	public void DrawView( CameraComponent camera, RenderTargetHandle target, ViewSetup viewSetup = default )
	{
		if ( !camera.IsValid() ) return;

		//The command was carried out by their renderer; ours has nothing to run.
	}

	/// <summary>Render a planar reflection using the specified camera and the specified plane.</summary>
	public void DrawReflection( CameraComponent camera, Plane plane, in RenderTargetHandle target, ReflectionSetup reflectionSetup = default )
	{
		if ( !camera.IsValid() ) return;

		//The command was carried out by their renderer; ours has nothing to run.
	}

	/// <summary>Render a planar refraction using the specified camera and the specified plane. This is for all intents and purposes a regular view with a plane clipping it. Usually used for rendering under water.</summary>
	public void DrawRefraction( CameraComponent camera, Plane plane, in RenderTargetHandle target, RefractionSetup refractionSetup = default )
	{
		if ( !camera.IsValid() ) return;

		//The command was carried out by their renderer; ours has nothing to run.
	}

}




