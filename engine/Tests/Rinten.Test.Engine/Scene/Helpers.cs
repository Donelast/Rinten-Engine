using System;
using Rinten.Utility;

namespace SceneTests;

#nullable enable

//Helper methods for writing scene tests.
internal static class Helpers
{
	//Registers a prefab file in ResourceLibrary with the given resourcePath and with a
	//root object defined by rootObjectJson. Dispose the return value to unregister it.
	public static IDisposable RegisterPrefabFromJson( string resourcePath, string rootObjectJson )
	{
		var wrappedRootObject = "{ \"RootObject\": " + rootObjectJson + "}";

		var prefabFile = new PrefabFile();
		prefabFile.LoadFromJson( wrappedRootObject );
		prefabFile.RegisterWeakResourceId( resourcePath );
		prefabFile.Register( resourcePath );

		return new DisposeAction( () => Game.Resources.Unregister( prefabFile ) );
	}

	//Creates a SceneFile with the given resourcePath, populates it with game objects as given by gameObjectsJson, then loads it with Scene.Load.
	public static Scene LoadSceneFromJson( string resourcePath, params string[] gameObjectsJson )
	{
		var scene = new Scene();

		using var _ = scene.Push();

		var sceneFile = new SceneFile { GameObjects = gameObjectsJson.Select( Rinten.Json.ParseToJsonObject ).ToArray() };

		sceneFile.RegisterWeakResourceId( resourcePath );

		var options = new SceneLoadOptions();
		options.SetScene( sceneFile );

		scene.Load( options );

		return scene;
	}
}
