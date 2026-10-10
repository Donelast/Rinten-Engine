namespace Editor;

//Viewport split layout picker on the toolbar; acts on this toolbar's view
partial class ViewportToolsPanel
{
	private void OpenLayoutMenu()
	{
		var view = sceneView;
		if ( !view.IsValid() ) return;

		var menu = new ContextMenu( null );

		foreach ( var entry in EditorTypeLibrary.GetEnumDescription( typeof( ViewportLayoutMode ) ) )
		{
			var layout = (ViewportLayoutMode)entry.ObjectValue;

			var o = menu.AddOption( entry.Title, entry.Icon, () => view.ViewportLayout = layout );
			o.Checkable = true;
			o.Checked = view.ViewportLayout == layout;
			//Play mode owns the game viewport until the session stops.
			o.Enabled = !view.Session.IsPlaying;
		}

		menu.OpenAtCursor();
	}
}
