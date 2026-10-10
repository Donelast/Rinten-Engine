using Editor.TreeBrowser;

namespace Editor;

[EditorTool( "tools.eye-dropper-tool", Hidden = true )]
public class EyeDropperTool : EditorTool
{
	static string LastTool;
	internal static SerializedProperty TargetProperty = null;
	internal static Action OnBackToLastTool;

	string LastSelection;

	public override void OnEnabled()
	{
		base.OnEnabled();

		SubscribeEvents();

		AllowGameObjectSelection = true;
		LastSelection = Manager.CurrentSession.SerializeSelection();

		SetCrosshairCursor( true );
	}

	public override void OnDisabled()
	{
		base.OnDisabled();

		UnsubscribeEvents();
		SetCrosshairCursor( false );
	}

	void SubscribeEvents()
	{
		Selection.OnItemAdded += OnItemAdded;
		TreeAssetBrowser.AssetHighlighted += OnItemAdded;
		Application.OnWidgetClicked += OnWidgetPressed;
	}

	void UnsubscribeEvents()
	{
		Selection.OnItemAdded -= OnItemAdded;
		TreeAssetBrowser.AssetHighlighted -= OnItemAdded;
		Application.OnWidgetClicked -= OnWidgetPressed;
	}

	void OnItemAdded( object obj )
	{
		Select( obj );
		UnsubscribeEvents();
		Manager.CurrentSession.DeserializeSelection( LastSelection );
	}

	void OnWidgetPressed( Widget widget, MouseEvent mouseEvent )
	{
	}

	internal static void Select( object obj )
	{
		if ( obj is GameObject gameObject )
		{
			ProcessObject( gameObject );
		}
		else if ( obj is Component component )
		{
			ProcessComponent( component );
		}
		else if ( obj is Asset asset )
		{
			ProcessAsset( asset );
		}
		BackToLastTool();
	}

	public override void OnUpdate()
	{
		base.OnUpdate();

		if ( !TargetProperty.Parent.Contains( TargetProperty ) )
		{
			BackToLastTool();
			return;
		}

		if ( Gizmo.WasLeftMouseReleased )
		{
			var tr = MeshTrace.Run();
			GameObject hitObject = null;
			if ( tr.Hit )
				hitObject = tr.GameObject;

			ProcessAfterSelection( hitObject );
		}
	}

	static void ProcessObject( GameObject obj )
	{
		if ( TargetProperty is null ) return;
		if ( !obj.IsValid() ) return;

		if ( TargetProperty.PropertyType == typeof( GameObject ) )
		{
			TargetProperty.SetValue( obj );
			return;
		}

		if ( TargetProperty.PropertyType.IsInstanceOfType( obj ) )
		{
			TargetProperty.SetValue( obj );
			return;
		}

		var comp = obj.Components.Get( TargetProperty.PropertyType, FindMode.EnabledInSelfAndDescendants );
		comp ??= obj.Components.Get( TargetProperty.PropertyType, FindMode.DisabledInSelfAndDescendants );
		ProcessComponent( comp );
	}

	static void ProcessComponent( Component comp )
	{
		if ( TargetProperty is null ) return;
		if ( !comp.IsValid() ) return;
		if ( !TargetProperty.PropertyType.IsInstanceOfType( comp ) ) return;
		TargetProperty.SetValue( comp );
	}

	static void ProcessAsset( Asset asset )
	{
		if ( TargetProperty is null || asset is null ) return;

		if ( TargetProperty.PropertyType == typeof( GameObject ) )
		{
			if ( asset.TryLoadResource( out PrefabFile prefabFile ) )
				ProcessObject( SceneUtility.GetPrefabScene( prefabFile ) );
			return;
		}

		if ( !TargetProperty.PropertyType.IsInterface ) return;
		if ( !asset.AssetType.IsGameResource ) return;
		if ( !asset.AssetType.ResourceType.IsAssignableTo( TargetProperty.PropertyType ) ) return;

		var resource = asset.LoadResource<GameResource>();
		if ( resource is null || !resource.IsValid() ) return;
		TargetProperty.SetValue( resource );
	}

	async void ProcessAfterSelection( GameObject obj )
	{
		await Task.Delay( 100 );

		if ( obj.IsValid() )
			ProcessObject( obj );

		BackToLastTool();
	}

	public static void SetTargetProperty( SerializedProperty property )
	{
		if ( EditorToolManager.CurrentModeName == nameof( EyeDropperTool ) )
		{
			BackToLastTool();
			return;
		}

		LastTool = EditorToolManager.CurrentModeName;
		EditorToolManager.SetTool( nameof( EyeDropperTool ) );
		TargetProperty = property;
	}

	internal static void BackToLastTool()
	{
		SetCrosshairCursor( false );

		if ( string.IsNullOrEmpty( LastTool ) )
			return;

		EditorToolManager.SetTool( LastTool );
		LastTool = null;
		TargetProperty = null;

		OnBackToLastTool?.Invoke();
	}

	static void SetCrosshairCursor( bool active )
	{
		var shape = active ? CursorShape.Cross : CursorShape.Arrow;

		if ( SceneDock.Active is { IsValid: true } dock )
		{
			dock.Cursor = shape;
			dock.PixmapCursor = null;
		}

		if ( SceneTreeWidget.Current is { IsValid: true } tree )
			tree.Cursor = active ? CursorShape.Cross : CursorShape.None;
	}
}
