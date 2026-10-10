namespace Editor;

public static class VRToggle
{
	[Event( "editor.titlebar.buttons.build", Priority = 10 )]
	public static void OnBuildTitleBarButtons( TitleBarButtons titleBarButtons )
	{
		if ( EditorUtility.VR.HasHeadset )
		{
			var button = titleBarButtons.AddToggleButton( "vrpano", ( v ) => EditorUtility.VR.Enabled = v, EditorUtility.VR.Enabled );
			button.ToolTip = "Toggle VR";
		}
	}
}
