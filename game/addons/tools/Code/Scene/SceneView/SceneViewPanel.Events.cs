namespace Editor;

public partial class SceneViewPanel : ResourceLibrary.IEventListener
{
	private EditorModal externalChangesDialog;

	void ResourceLibrary.IEventListener.OnExternalChanges( GameResource resource )
	{
		if ( resource != Session.Scene.Source ) return;

		if ( externalChangesDialog.IsValid() ) //already showing one
			return;

		TabSession.MakeActive();

		string filename = System.IO.Path.GetFileName( Session.Scene.Source.ResourcePath );

		externalChangesDialog = EditorModal.Choose( this, "External Changes Detected",
			$"The asset '{filename}' has been modified outside of the Rinten editor.\n\nReload discards any unsaved changes and loads the new version.\nKeep My Work overwrites the external changes with your working copy.",
			null,
			new[]
			{
				new EditorModal.Choice( "Reload", () => Session.Reload(), Destructive: true ),

				//Save what we have already, discarding any external changes.
				new EditorModal.Choice( "Keep My Work", () => Session.Save( false ) ),
			} );
	}
}
