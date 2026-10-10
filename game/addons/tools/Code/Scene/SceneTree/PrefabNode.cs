using Rinten.Diagnostics;

namespace Editor;

partial class PrefabNode : GameObjectNode
{
	public PrefabNode( PrefabScene go ) : base( go )
	{
	}

	public override bool OnContextMenu()
	{
		var m = new ContextMenu( MenuHost );

		m.AddOption( "Save Prefab", action: () => Save( Value, false ) );
		m.AddOption( "Save As..", action: () => Save( Value, true ) );

		m.AddSeparator();

		AddGameObjectMenuItems( m, this );

		m.OpenAtCursor( false );

		return true;
	}

	public static void Save( GameObject obj, bool saveAs )
	{
		var scene = obj.Scene;
		var saveLocation = "";

		Assert.True( scene is PrefabScene prefabScene );
		prefabScene = scene as PrefabScene;

		var prefabFile = prefabScene.ToPrefabFile();

		var asset = AssetSystem.FindByPath( prefabScene.Source.ResourcePath );
		if ( asset is not null )
		{
			saveLocation = asset.AbsolutePath;
		}

		if ( saveAs )
		{
			var lastDirectory = Game.Cookies.GetString( "LastSaveSceneLocation", "" );

			var fd = new FileDialog( null );
			fd.Title = $"Save Prefab As..";
			fd.Directory = lastDirectory;
			fd.DefaultSuffix = $".prefab";
			fd.SelectFile( saveLocation );
			fd.SetFindFile();
			fd.SetModeSave();
			fd.SetNameFilter( $"Prefab (*.prefab)" );

			if ( !fd.Execute() )
				return;

			saveLocation = fd.SelectedFile;

			//Somewhere else, so the asset is the one there - not the file it was opened from, which "Save As" used to overwrite whatever
			//name was chosen. It offered .object too, the legacy name for a prefab, under a filter that hid every .prefab in the folder.
			asset = AssetSystem.FindByPath( saveLocation );
		}

		if ( asset is null )
		{
			asset = AssetSystem.CreateResource( "prefab", saveLocation );
		}
		asset.SaveToDisk( prefabFile );
	}
}
