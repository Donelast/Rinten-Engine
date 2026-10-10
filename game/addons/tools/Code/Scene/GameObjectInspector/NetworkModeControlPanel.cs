namespace Editor.Inspectors;

using Editor;

/// <summary>Network mode picker for the GameObject header.</summary>
public class NetworkModeControlPanel : Panel
{
	public SerializedObject Target
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			ApplyTarget();
		}
	}

	SerializedProperty modeProperty;
	ToolbarButton button;
	NetworkMode lastMode;

	protected override void OnParametersSet() => ApplyTarget();

	void ApplyTarget()
	{
		if ( Target is null || button is not null ) return;

		modeProperty = Target.GetProperty( nameof( GameObject.NetworkMode ) );
		AddClass( "network-mode" );

		button = AddChild<ToolbarButton>();
		button.ToolTip = "View Network Settings";
		button.OnPressed = OpenMenu;
		UpdateIcon();
	}

	public override void Tick()
	{
		base.Tick();
		if ( modeProperty is null ) return;
		var mode = modeProperty.GetValue<NetworkMode>();
		if ( mode != lastMode ) UpdateIcon();
	}

	void UpdateIcon()
	{
		if ( modeProperty is null ) return;
		lastMode = modeProperty.GetValue<NetworkMode>();
		button.Icon = lastMode switch
		{
			NetworkMode.Object => "wifi",
			NetworkMode.Snapshot => "network_wifi_2_bar",
			_ => "wifi_off"
		};
		button.Active = lastMode != NetworkMode.Never;
		button.StateHasChanged();
	}

	void OpenMenu()
	{
		if ( modeProperty is null ) return;

		var gameObjects = Target.Targets.Cast<GameObject>().ToArray();
		var menu = new Menu();
		var options = EditorTypeLibrary.GetEnumDescription( typeof( NetworkMode ) );

		foreach ( var entry in options )
		{
			var mode = (NetworkMode)entry.ObjectValue;
			var option = menu.AddOption( entry.Title, entry.Icon, () => SetMode( gameObjects, mode ) );
			option.Checkable = true;
			option.Checked = lastMode == mode;
		}

		menu.OpenAtCursor( this );
	}

	static void SetMode( GameObject[] gameObjects, NetworkMode mode )
	{
		var go = gameObjects.FirstOrDefault();
		if ( !go.IsValid() ) return;

		var session = SceneEditorSession.Resolve( go );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( "Network Mode" ).WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var obj in gameObjects )
				obj.NetworkMode = mode;
		}
	}
}
