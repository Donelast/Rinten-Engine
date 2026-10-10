using Rinten.UI;

namespace Editor.Gallery;

//Things drawn by something other than the panel renderer, worn by a panel: scenes, gizmos, a web page.
static class SceneExamples
{
	const string Page = "Scene";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Gizmo tests" ), Icon( "science" )]
	[Description( "Every ISceneTest in the type library, run one at a time in a scene panel: arrows, lines, position, rotation, scale, depth. Right-drag flies the camera." )]
	static Panel GizmoTests() => new GizmoScenePanel();

	//Six scenes, six cameras, six panels. Drag one to spin it. SceneRenderCore lets any panel wear a scene:
	//the scene is drawn once a frame and the picture is the panel's background. Six at once is the stress test.
	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Render to panel" ), Icon( "portrait" )]
	static Panel RenderToPanel()
	{
		var example = Example.Row();

		for ( var i = 0; i < 6; i++ )
			example.AddChild( new ModelSpinPanel( "models/citizen/citizen.vmdl" ) );

		return example;
	}

}
