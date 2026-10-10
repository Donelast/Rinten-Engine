using Rinten.UI;

namespace Editor;

/// <summary>Toolbar button that opens the view settings popup.</summary>
public sealed class ViewSettingsButton : PopupButton
{
	public SceneViewPanel SceneView { get; set; }

	public ViewSettingsButton()
	{
		RemoveClass( "button" );
		AddClass( "toolbarbutton" );
		Icon = "tune";
		Tooltip = "View Settings";
	}

	//The popup keeps the width its own sheet gives it, not the button's.
	protected override bool StretchPopupToWidth => false;

	public override void Open()
	{
		var viewport = SceneView?.LastSelectedViewport;
		if ( !viewport.IsValid() ) return;

		Popup = ViewSettingsPopup.Open( this, viewport );
	}
}
