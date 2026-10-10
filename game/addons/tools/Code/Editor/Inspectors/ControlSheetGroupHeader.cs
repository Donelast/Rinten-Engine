using Editor;

namespace Editor.Inspectors;

/// <summary>The bar at the top of a property group. What it adds to a fold header is the group's own business: the checkbox that enables it, the mark saying a prefab value was overridden, and a menu of what can be done to the properties inside.</summary>
[StyleSheet( "ControlSheetGroupHeader.cs.scss" )]
public class ControlSheetGroupHeader : PanelFoldHeader
{
	public SerializedProperty ToggleProperty
	{
		get;
		set
		{
			if ( field == value ) return;
			field = value;
			RebuildToggle();
		}
	}

	ControlSheetGroup group;
	Panel prefabBar;
	BaseControl toggleControl;

	public ControlSheetGroupHeader( ControlSheetGroup group )
	{
		this.group = group;
		//The group wears the plate skin: a round chevron and no band of its own.
		AddClass( "plate" );
		AddClass( "controlsheetgroupheader" );

		prefabBar = AddChild<Panel>( "prefab-bar" );
		prefabBar.AddClass( "group-prefab-bar" );
		prefabBar.AddClass( "hidden" );
	}

	public override void Tick()
	{
		base.Tick();

		SetClass( "hidden", string.IsNullOrWhiteSpace( Title ) && ToggleProperty is null );

		if ( prefabBar.IsValid() && ToggleProperty is not null )
			prefabBar.SetClass( "hidden", !EditorUtility.Prefabs.IsPropertyOverridden( ToggleProperty ) );
	}

	//Ticking the box that enables the group is not asking for the group to close, and right-clicking it is the control's menu rather than ours.
	protected override bool IsFoldingClick( Panel target ) => target?.IsAncestor( toggleControl ) != true;

	void RebuildToggle()
	{
		if ( toggleControl.IsValid() )
		{
			toggleControl.Delete( true );
			toggleControl = null;
		}

		SetClass( "has-toggle", ToggleProperty is not null );
		if ( ToggleProperty is null ) return;

		toggleControl = PanelControlFactory.CreateFor( ToggleProperty, wrapNullable: false );
		if ( toggleControl is null ) return;

		AddChild( toggleControl );
	}

	protected override void OnContextMenu()
	{
		if ( group?.Properties is null || group.Properties.Count == 0 ) return;

		var menu = new Menu();
		var name = Title;

		//Nothing in the group can be written, so neither of the two that write can be chosen.
		var editable = group.Properties.Any( x => !PropertyContextMenu.IsReadOnly( x ) );

		menu.AddOption( $"Copy {name} Properties", "content_copy", () =>
		{
			ClipboardTools.CopyProperties( name, group.Properties );
		} );

		var paste = menu.AddOption( $"Paste {name} Properties", "content_paste", () =>
		{
			ClipboardTools.PasteProperties( name, group.Properties );
		} );
		paste.Enabled = editable && ClipboardTools.CanPasteProperties( name, group.Properties );

		menu.AddOption( "Reset all to Default", "restart_alt", () =>
		{
			foreach ( var prop in group.Properties )
			{
				if ( PropertyContextMenu.IsReadOnly( prop ) ) continue;

				prop.SetValue( prop.GetDefault() );
			}
		} ).Enabled = editable;

		menu.OpenAtCursor( this );
	}
}
