namespace Rinten.UI;

public partial class Panel
{
	//The scene we're currently registered in (if any), so we know what to remove ourselves from.
	Scene _indexedScene;

	//Keep this panel registered in its scene's object index, so it can be found by Scene.GetAll<T> / Scene.RunEvent<T>
	// / ISceneEvent<T> exactly like a component. Idempotent and cheap - a no-op while our scene hasn't changed.
	void UpdateSceneIndex()
	{
		var scene = Scene;
		if ( scene == _indexedScene )
			return;

		_indexedScene?.RemoveObjectFromDirectory( this );
		_indexedScene = scene;
		_indexedScene?.AddObjectToDirectory( this );
	}

	//Drop ourselves from the scene object index. Called when the panel is deleted.
	void RemoveFromSceneIndex()
	{
		if ( _indexedScene is null )
			return;

		_indexedScene.RemoveObjectFromDirectory( this );
		_indexedScene = null;
	}
}
