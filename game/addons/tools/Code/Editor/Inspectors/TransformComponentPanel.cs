namespace Editor.Inspectors;

[StyleSheet( "TransformComponentPanel.cs.scss" )]
[CustomEditor( typeof( GameTransform ) )]
public class TransformComponentPanel : ComponentPanel
{
	Panel body;

	bool UseLocal
	{
		get => ProjectCookie.Get( "Transform.UseLocal", true );
		set => ProjectCookie.Set( "Transform.UseLocal", value );
	}

	public TransformComponentPanel( SerializedObject obj )
	{
		TargetObject = obj;
		body = AddChild<Panel>( "body" );
		Rebuild();
	}

	private BoneBanner banner;

	//Edited object from GameTransform itself; serialized targets made from instances have no parent
	private GameObject TargetGameObject
	{
		get
		{
			if ( TargetObject is { IsMultipleTargets: false } target
				&& target.Targets.OfType<GameTransform>().FirstOrDefault() is { } transform )
			{
				return transform.GameObject;
			}

			if ( GameObjectObject is not { IsMultipleTargets: false } obj ) return null;

			return obj.Targets.OfType<GameObject>().FirstOrDefault();
		}
	}

	//The flags of the object being edited.
	private GameObjectFlags ObjectFlags => TargetGameObject?.Flags ?? default;

	//Bone flag set at runtime by SkinnedModelRenderer.BuildBoneHierarchy; fix that if this is wrong
	private bool IsBone => ObjectFlags.Contains( GameObjectFlags.Bone );

	private bool IsLoose => ObjectFlags.Contains( GameObjectFlags.ProceduralBone );

	/// <summary>A bone's numbers are the animation's until somebody says otherwise.</summary>
	public override void Tick()
	{
		base.Tick();

		var isBone = IsBone;

		if ( banner.IsValid() )
		{
			banner.SetClass( "hidden", !isBone );
			banner.Refresh( IsLoose );
		}

		body?.SetClass( "locked", isBone && !IsLoose );
	}

	void Rebuild()
	{
		body.DeleteChildren();

		banner = AddChild<BoneBanner>();
		banner.Owner = this;
		banner.SetClass( "hidden", !IsBone );

		//Above the numbers it explains, and the rows are already in `body`.
		banner.Parent?.SetChildIndex( banner, 0 );

		if ( UseLocal )
		{
			AddRow( TargetObject.GetProperty( "LocalPosition" ) );
			AddRow( TargetObject.GetProperty( "LocalRotation" ) );
			AddRow( TargetObject.GetProperty( "LocalScale" ) );
		}
		else
		{
			AddRow( TargetObject.GetProperty( "Position" ) );
			AddRow( TargetObject.GetProperty( "Rotation" ) );
			AddRow( TargetObject.GetProperty( "Scale" ) );
		}
	}

	void AddRow( SerializedProperty prop )
	{
		if ( prop is null ) return;
		var row = body.AddChild<ControlSheetRow>();
		row.Initialize( prop );
	}

	SerializedObject GameObjectObject => TargetObject?.ParentProperty?.Parent;

	public override bool FillHeaderContextMenu( Menu menu )
	{
		menu.AddOption( UseLocal ? "Display Worldspace" : "Display Localspace", "public", () =>
		{
			UseLocal = !UseLocal;
			Rebuild();
		} );

		menu.AddSeparator();

		menu.AddOption( "Reset", "restart_alt", () => DoTransformEdit( "Reset Transform",
			() => TargetObject.GetProperty( UseLocal ? "Local" : "World" ).SetValue( Transform.Zero ) ) );

		menu.AddSeparator();

		menu.AddOption( "Copy Local Transform", "content_copy", () =>
		{
			var tx = TargetObject.GetProperty( "Local" ).GetValue<Transform>();
			EditorUtility.Clipboard.Copy( Json.Serialize( tx ) );
		} );

		menu.AddOption( "Copy World Transform", "content_copy", () =>
		{
			var tx = TargetObject.GetProperty( "World" ).GetValue<Transform>();
			EditorUtility.Clipboard.Copy( Json.Serialize( tx ) );
		} );

		try
		{
			var clipText = EditorUtility.Clipboard.Paste();
			var tx = Json.Deserialize<Transform>( clipText );
			if ( tx != default )
			{
				menu.AddOption( "Paste as Local Transform", "content_paste",
					() => DoTransformEdit( "Paste Local Transform", () => TargetObject.GetProperty( "Local" ).SetValue( tx ) ) );

				menu.AddOption( "Paste as World Transform", "content_paste",
					() => DoTransformEdit( "Paste World Transform", () => TargetObject.GetProperty( "World" ).SetValue( tx ) ) );
			}
		}
		catch ( Exception )
		{
			//Clipboard was not a transform.
		}

		return true;
	}

	//Take this bone off the animation, or give it back.
	internal void ToggleLoose()
	{
		//Through TargetGameObject for the same reason it exists - see there.
		var targets = GameObjectObject?.Targets.OfType<GameObject>().ToArray() ?? [];

		if ( targets.Length == 0 && TargetGameObject is { IsValid: true } single )
			targets = [single];

		if ( targets.Length == 0 ) return;

		var loose = IsLoose;
		var session = SceneEditorSession.Resolve( targets[0] );
		if ( session is null ) return;

		using var scene = session.Scene.Push();

		using ( session.UndoScope( loose ? "Return Bone To Animation" : "Make Bone Procedural" )
			.WithGameObjectChanges( targets, GameObjectUndoFlags.Properties )
			.Push() )
		{
			foreach ( var go in targets )
			{
				go.Flags = go.Flags.WithFlag( GameObjectFlags.ProceduralBone, !loose );
			}
		}
	}

	void DoTransformEdit( string name, Action action )
	{
		//The same fallback as ToggleLoose: once the sheet has re-serialized its target there is no parent
		//property to walk, and without this the header menu's Reset and Paste quietly did nothing at all.
		var targets = GameObjectObject?.Targets.OfType<GameObject>().ToArray() ?? [];

		if ( targets.Length == 0 && TargetGameObject is { IsValid: true } single )
			targets = [single];

		if ( targets.Length == 0 ) return;

		var session = SceneEditorSession.Resolve( targets.FirstOrDefault() );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( name ).WithGameObjectChanges( targets, GameObjectUndoFlags.Properties ).Push() )
			action();
	}
}

//Banner over bone fields: pink while animated, green once ProceduralBone frees it, with a toggle
internal sealed class BoneBanner : Panel
{
	public TransformComponentPanel Owner { get; set; }

	//Qualified: this file's namespace is Editor.Inspectors, where a bare Label is Editor.Label - a Widget whose
	//second constructor argument is its parent widget, not a class name. The panel is the one wanted here.
	private readonly Rinten.UI.Label label;

	public BoneBanner()
	{
		AddClass( "bone-banner" );

		AddChild( new IconPanel( "polyline", "icon" ) );

		label = AddChild( new Rinten.UI.Label( "", "text" ) );

		AddEventListener( "onclick", () => Owner?.ToggleLoose() );
	}

	public void Refresh( bool loose )
	{
		SetClass( "loose", loose );

		label.Text = loose
			? "Moved by hand - click to give it back to the animation"
			: "Moved by the animation - click to move it yourself";
	}
}
