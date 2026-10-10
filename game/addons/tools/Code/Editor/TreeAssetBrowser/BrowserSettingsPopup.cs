using Rinten.UI;

namespace Editor.TreeBrowser;

[StyleSheet( "BrowserSettingsPopup.cs.scss" )]
sealed class BrowserSettingsPopup : Popup
{
	readonly IconPanel check;

	public static BrowserSettingsPopup Open( Panel anchor )
	{
		if ( anchor is null || !anchor.IsValid() )
			return null;

		return new BrowserSettingsPopup( anchor );
	}

	BrowserSettingsPopup( Panel anchor )
	{
		SetPositioning( anchor, PositionMode.BelowRight, 4 );
		AnchorPanel = anchor;
		CloseWhenParentIsHidden = true;
		AddClass( "editor-popup" );
		AddClass( "browser-settings-popup" );

		var body = AddChild<Panel>( "body" );

		var heading = body.AddChild<Rinten.UI.Label>( "heading" );
		heading.AddClass( "heading" );
		heading.Text = "Browser Settings";

		var row = body.AddChild<Panel>( "menu-row" );
		row.AddClass( "menu-row" );
		row.AddChild<Rinten.UI.Label>( "caption" ).Text = "Show icons in Tree View";
		check = row.AddChild<IconPanel>( "check" );
		check.Text = "check";

		row.AddEventListener( "onmousedown", _ =>
		{
			BrowserSettings.ShowTreeIcons = !BrowserSettings.ShowTreeIcons;
			SyncCheck();
		} );

		SyncCheck();
		SyncAnchor();
	}

	public override void Tick()
	{
		base.Tick();
		SyncCheck();
	}

	void SyncCheck() => check.Text = BrowserSettings.ShowTreeIcons ? "check" : "";
}
