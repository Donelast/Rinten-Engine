namespace Editor;

using Editor.Inspectors;

/// <summary>Embeds a panel <see cref="BaseControl"/> in a widget layout slot.</summary>
[CustomEditor( typeof( GameObject ) )]
[CustomEditor( typeof( Component ) )]
[CustomEditor( typeof( Enum ), WithAllAttributes = [typeof( EnumButtonGroupAttribute )] )]
[CustomEditor( ForInterface = true )]
public sealed class PanelControlWidget : ControlWidget, IPanelControlHost
{
	BaseControl _control;
	bool _mounted;

	/// <summary>False when nothing but the generic panel fallbacks answered for this property.</summary>
	public bool HasPanelControl { get; }

	public override bool SupportsMultiEdit => _control?.SupportsMultiEdit ?? false;
	public override bool IsWideMode => _control?.IsWideMode ?? base.IsWideMode;
	public override bool IncludeLabel => _control?.IncludeLabel ?? base.IncludeLabel;

	//Whatever the panel in it makes it: an enum as a row of buttons, a wide control
	//under its name. Not a row's height by default - see ControlWidget.IsOneRow.
	protected override bool IsOneRow => false;

	public PanelControlWidget( SerializedProperty property ) : base( property )
	{
		PaintBackground = false;
		HorizontalSizeMode = SizeMode.CanGrow;
		HasPanelControl = PanelControlFactory.HasEditorFor( property );
		_control = PanelControlFactory.CreateFor( property );
	}

	public override void SyncSurfaceContent()
	{
		base.SyncSurfaceContent();

		if ( _control is null || ContentPanel is null || _mounted ) return;

		ContentPanel.AddClass( "widget-panel-control" );
		ContentPanel.AddChild( _control );
		_mounted = true;
	}
}
