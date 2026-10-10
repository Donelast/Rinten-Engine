namespace Editor.Inspectors;

using Editor;

/// <summary>Add Component button under the component list.</summary>
[StyleSheet( "AddComponentPanel.cs.scss" )]
public class AddComponentPanel : Panel
{
	const string OpenSelectorEvent = "open-selector";
	const string AddComponentEvent = "add-component";

	static TypeDescription pendingType;
	static SerializedObject pendingTarget;

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

	Panel button;
	ComponentTypeSelectorPopup selectorPopup;

	protected override void OnParametersSet() => ApplyTarget();

	void ApplyTarget()
	{
		if ( Target is null || button is not null ) return;

		AddClass( "add-component-panel" );
		AddEventListener( OpenSelectorEvent, OpenSelector );
		AddEventListener( AddComponentEvent, ProcessPendingAdd );

		button = AddChild<Panel>( "button" );
		button.AddClass( "btn" );
		button.AddClass( "good" );
		button.AddEventListener( "onclick", QueueOpenSelector );

		var icon = button.AddChild( new IconPanel( "add", "icon" ) );

		var label = button.AddChild( new Rinten.UI.Label( "Add Component", "label" ) );
	}

	void QueueOpenSelector() => CreateEvent( OpenSelectorEvent, null, 0.001f );

	public override void Tick()
	{
		base.Tick();

		if ( selectorPopup is not null && !selectorPopup.IsValid )
			selectorPopup = null;

		if ( button is not null )
		{
			var open = selectorPopup is { IsValid: true };
			button.SetClass( "open", open );
			button.SetClass( "active", open );
		}
	}

	void OpenSelector()
	{
		if ( Target is null || button is null ) return;

		if ( selectorPopup is { IsValid: true } )
		{
			selectorPopup.Delete();
			selectorPopup = null;
			return;
		}

		var target = Target;
		if ( !target.IsValid ) return;

		//the targets are taken now: creating a new component recompiles the project and the
		//inspector (this panel and its SerializedObject) may be rebuilt before the type comes back
		var gameObjects = target.Targets.OfType<GameObject>().Where( x => x.IsValid() ).ToArray();

		selectorPopup = ComponentTypeSelectorPopup.Open( button, type =>
		{
			if ( IsValid )
				QueueAddComponent( type, target );
			else
				AddComponent( type, gameObjects );
		} );
	}

	void QueueAddComponent( TypeDescription componentType, SerializedObject target )
	{
		pendingType = componentType;
		pendingTarget = target;
		CreateEvent( AddComponentEvent, null, 0.001f );
	}

	void ProcessPendingAdd()
	{
		if ( pendingType is null || pendingTarget is null ) return;

		AddComponent( pendingType, pendingTarget );

		pendingType = null;
		pendingTarget = null;
	}

	static void AddComponent( TypeDescription componentType, SerializedObject target )
	{
		if ( componentType is null || target is null || !target.IsValid ) return;

		AddComponent( componentType, target.Targets.OfType<GameObject>().ToArray() );
	}

	static void AddComponent( TypeDescription componentType, GameObject[] targets )
	{
		if ( componentType is null ) return;

		var gameObject = targets.FirstOrDefault( x => x.IsValid() );
		if ( !gameObject.IsValid() ) return;

		var session = SceneEditorSession.Resolve( gameObject );
		if ( session is null ) return;

		using var scene = session.Scene.Push();
		using ( session.UndoScope( "Add Component(s)" ).WithComponentCreations().Push() )
		{
			foreach ( var go in targets )
				if ( go.IsValid() )
					go.Components.Create( componentType );
		}
	}

}
