using Rinten.UI;

namespace Editor.Gallery;

//The smallest thing that proves a scene can be rendered into a panel: its own scene, its own camera, ticked and drawn by SceneRenderCore like every viewport. Six of these on one page is the render-to-surface stress test.
/// <summary>A model in a lit scene, drawn into a panel, spun by dragging.</summary>
[StyleSheet( "ModelSpinPanel.cs.scss" )]
public sealed class ModelSpinPanel : SceneRenderingPanel
{
	readonly AmbientLight ambient;
	readonly CameraComponent camera;
	readonly SkinnedModelRenderer model;

	float hoverTime;
	float spinVelocity;
	Vector2 lastPosition;

	public ModelSpinPanel( string modelName )
	{
		AddClass( "model-spin" );

		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			camera.BackgroundColor = Color.Black;
			camera.Enabled = true;

			var warm = new GameObject( true, "light" ).GetOrAddComponent<PointLight>( false );
			warm.WorldPosition = 2.54f;
			warm.Radius = 12.7f;
			warm.LightColor = Color.Orange * 3;
			warm.Enabled = true;

			var cold = new GameObject( true, "light" ).GetOrAddComponent<PointLight>( false );
			cold.WorldPosition = -2.54f;
			cold.Radius = 12.7f;
			cold.LightColor = Color.Cyan * 3;
			cold.Enabled = true;

			ambient = new GameObject( true, "ambient" ).GetOrAddComponent<AmbientLight>( false );
			ambient.Color = Color.Black;
			ambient.Enabled = true;

			model = new GameObject( true, "model" ).GetOrAddComponent<SkinnedModelRenderer>( false );
			model.Model = Model.Load( modelName );
			model.WorldPosition = Vector3.Down * model.Model.Bounds.Mins.y;
			model.Enabled = true;

			UpdateCamera();
		}

		Camera = camera;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		Scene?.Destroy();
		Scene = null;
	}

	//Whether the left button went down over the picture and has not come up yet.
	bool dragging;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		dragging = true;
		lastPosition = e.LocalPosition;
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton == MouseButtons.Left )
			dragging = false;
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );

		dragging = false;
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );

		//A move event names no button, so the press is remembered rather than read.
		var delta = e.LocalPosition - lastPosition;
		lastPosition = e.LocalPosition;

		if ( dragging )
			spinVelocity += delta.x * 0.1f;
	}

	protected override void PreFrame()
	{
		base.PreFrame();

		if ( !Scene.IsValid() ) return;

		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		spinVelocity = spinVelocity.Approach( 0.0f, RealTime.Delta * 10.0f );
		hoverTime += spinVelocity * RealTime.Delta;

		if ( ambient.IsValid() )
			ambient.Color = IsUnderMouse ? Color.Gray : Color.Black;

		UpdateCamera();

		Gizmo.Draw.Grid( 0, Gizmo.GridAxis.ZX );
	}

	void UpdateCamera()
	{
		if ( !camera.IsValid() || !model.IsValid() ) return;

		var direction = new Vector3( MathF.Sin( hoverTime ), MathF.Cos( hoverTime ), 0.5f ).Normal;
		var distance = MathX.SphereCameraDistance( model.Bounds.Size.Length * 0.5f, camera.FieldOfView );
		var size = ScreenSize;
		var aspect = size.y > 0 ? size.x / size.y : 1;

		if ( aspect > 1 ) distance *= aspect;

		camera.WorldPosition = model.Bounds.Center + direction * distance * 0.9f;
		camera.WorldRotation = Rotation.LookAt( direction * -1.0f, Vector3.Up );
		camera.ZFar = 254f;
		camera.FieldOfView = 50;
	}
}
