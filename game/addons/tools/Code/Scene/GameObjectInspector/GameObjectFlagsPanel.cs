namespace Editor.Inspectors;

using Editor;

/// <summary>GameObject advanced flags menu trigger.</summary>
public class GameObjectFlagsPanel : Panel
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

	SerializedProperty flags;
	ToolbarButton button;
	Menu flagsMenu;

	protected override void OnParametersSet() => ApplyTarget();

	void ApplyTarget()
	{
		if ( Target is null || flags is not null ) return;

		flags = Target.GetProperty( nameof( GameObject.Flags ) );
		AddClass( "gameobject-flags" );

		button = AddChild<ToolbarButton>();
		button.Icon = "expand_more";
		button.ToolTip = "Advanced Flags";
		button.OnPressed = OpenMenu;
	}

	public override void Tick()
	{
		base.Tick();

		if ( flagsMenu is not null && !flagsMenu.IsValid )
			flagsMenu = null;
	}

	void OpenMenu()
	{
		if ( flags is null || button is null ) return;

		if ( flagsMenu is { IsValid: true } )
		{
			flagsMenu.Close();
			flagsMenu = null;
			return;
		}

		var value = flags.GetValue<GameObjectFlags>();
		var menu = new Menu();
		flagsMenu = menu;

		var editorOnly = menu.AddOption( "Editor Only", null, () => Toggle( GameObjectFlags.EditorOnly ) );
		editorOnly.Checkable = true;
		editorOnly.Checked = value.Contains( GameObjectFlags.EditorOnly );

		var absolute = menu.AddOption( "Position Absolute", null, () => Toggle( GameObjectFlags.Absolute ) );
		absolute.Checkable = true;
		absolute.Checked = value.Contains( GameObjectFlags.Absolute );

		menu.OpenBelowRight( button, 4f );
	}

	void Toggle( GameObjectFlags bit )
	{
		var value = flags.GetValue<GameObjectFlags>();
		flags.SetValue( value.WithFlag( bit, !value.Contains( bit ) ) );
	}
}
