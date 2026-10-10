using Rinten.UI;

namespace Editor;

/// <summary>Toolbar button that opens the network settings popup.</summary>
public sealed class NetworkSettingsButton : PopupButton
{
	public NetworkSettingsButton()
	{
		RemoveClass( "button" );
		AddClass( "toolbarbutton" );
		Icon = "wifi";
		Tooltip = "Network settings";
	}

	//The popup keeps the width its own sheet gives it, not the button's.
	protected override bool StretchPopupToWidth => false;

	public override void Open() => Popup = NetworkSettingsPopup.Open( this );
}
