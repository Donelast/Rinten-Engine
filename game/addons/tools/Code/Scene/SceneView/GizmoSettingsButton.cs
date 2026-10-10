using Rinten.UI;

namespace Editor;

/// <summary>Narrow toolbar control that opens the gizmo visibility popup.</summary>
public sealed class GizmoSettingsButton : PopupButton
{
	public GizmoSettingsButton()
	{
		RemoveClass( "button" );
		AddClass( "toolbarbutton" );
		AddClass( "narrow" );
		Icon = "arrow_drop_down";
		Tooltip = "Gizmo Settings...";
	}

	//The button is 12px wide; the popup keeps the width its own sheet gives it.
	protected override bool StretchPopupToWidth => false;

	public override void Open() => Popup = GizmoSettingsPopup.Open( this );
}
