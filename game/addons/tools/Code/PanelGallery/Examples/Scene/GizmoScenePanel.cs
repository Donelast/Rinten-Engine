using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

/// <summary>A scene in a panel with a list of gizmo scenarios to run in it</summary>
[StyleSheet( "GizmoScenePanel.cs.scss" )]
public sealed class GizmoScenePanel : EditorPanel
{
	readonly Scene scene;
	readonly CameraComponent camera;
	readonly SceneRenderingPanel view;
	readonly List<(TypeDescription Type, Panel Row)> rows = new();

	ISceneTest current;
	TypeDescription currentType;

	public GizmoScenePanel()
	{
		AddClass( "gizmo-scene" );

		scene = Scene.CreateEditorScene();

		using ( scene.Push() )
		{
			camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>( false );
			camera.BackgroundColor = Theme.Blue;
			camera.ZFar = 104.0384f;
			camera.Enabled = true;

			var ambient = new GameObject( true, "ambient" ).GetOrAddComponent<AmbientLight>( false );
			ambient.Color = Theme.Blue * 0.4f;
			ambient.Enabled = true;

			var sun = new GameObject( true, "sun" ).GetOrAddComponent<DirectionalLight>( false );
			sun.WorldRotation = Rotation.From( 45, 45, 0 );
			sun.LightColor = Color.White;
			sun.Enabled = true;
		}

		view = AddChild( new SceneRenderingPanel { Scene = scene, Camera = camera } );
		view.AddClass( "view" );
		view.OnPreFrame += PreFrame;

		var list = AddChild<Panel>( "tests" );

		var types = EditorTypeLibrary.GetTypes<ISceneTest>()
			.Where( x => !x.IsInterface && !x.IsAbstract )
			.OrderBy( x => x.Title )
			.ToList();

		foreach ( var type in types )
		{
			var row = list.AddChild<Panel>( "list-row pressable" );

			var icon = row.AddChild<Label>( "material-icon" );
			icon.Text = string.IsNullOrEmpty( type.Icon ) ? "science" : type.Icon;

			var title = row.AddChild<Label>( "title" );
			title.Text = type.Title;

			row.Tooltip = type.Description;
			row.AddEventListener( "onclick", _ => Switch( type ) );

			rows.Add( (type, row) );
		}

		if ( types.Count > 0 )
			Switch( types[0] );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		view.OnPreFrame -= PreFrame;

		scene?.Destroy();
	}

	void Switch( TypeDescription type )
	{
		currentType = type;
		current = type.Create<ISceneTest>();

		camera.FieldOfView = 70;
		current?.Initialize( camera );

		foreach ( var (rowType, row) in rows )
			row.SetClass( "is-selected", rowType == type );
	}

	void PreFrame()
	{
		var instance = view.GizmoInstance;
		if ( instance is null ) return;

		instance.Input.IsHovered = view.IsActiveWindow && view.IsUnderMouse;

		if ( instance.FirstPersonCamera( camera, view ) )
			instance.Input.IsHovered = false;

		view.UpdateGizmoInputs( instance.Input.IsHovered );

		if ( Gizmo.ControlMode == "firstperson" )
		{
			Gizmo.Draw.Color = Gizmo.HasHovered ? Color.White : Color.Black.WithAlpha( 0.3f );
			Gizmo.Draw.LineSphere( new Sphere( camera.WorldPosition + camera.WorldRotation.Forward * 50.0f, 0.1f ) );
		}

		if ( current is not null )
		{
			current.Frame();

			Gizmo.Draw.Color = Color.Black;
			Gizmo.Draw.ScreenText( currentType?.Description ?? "", new Vector2( 30, Gizmo.Camera.Size.y - 30.0f ), flags: TextFlag.LeftBottom );
		}

		view.Cursor = Gizmo.HasHovered ? CursorShape.Finger : CursorShape.Arrow;
	}
}
