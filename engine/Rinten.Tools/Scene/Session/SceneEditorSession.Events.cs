using Rinten.Navigation;

namespace Editor;

public partial class SceneEditorSession : ResourceLibrary.IEventListener, NavMesh.IEventListener
{
	void ResourceLibrary.IEventListener.OnRegister( GameResource resource )
	{
		if ( resource is not PrefabFile prefab ) return;

		//Writing a prefab registers it, and what it holds then is exactly what the
		//propagation gave its instances a moment earlier - see EditorScene.NeedsHandingOut.
		if ( !EditorScene.NeedsHandingOut( prefab ) ) return;

		EditorScene.UpdatePrefabInstancesInScene( Scene, prefab );
	}

	void ResourceLibrary.IEventListener.OnUnregister( GameResource resource )
	{
		if ( resource is not PrefabFile prefab ) return;

		EditorScene.UpdatePrefabInstancesInScene( Scene, prefab );
	}

	void ResourceLibrary.IEventListener.OnExternalChangesPostLoad( GameResource resource )
	{
		if ( resource is not PrefabFile prefab ) return;

		EditorScene.UpdatePrefabInstancesInScene( Scene, prefab );
	}

	void NavMesh.IEventListener.OnAreaDefinitionChanged()
	{
		Scene.NavMesh?.UpdateAreaIds();
	}
}
