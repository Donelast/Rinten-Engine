using Facepunch.ActionGraphs;
using Rinten.ActionGraphs;
using Rinten.Internal;
using Rinten.Network;
using Rinten.Utility;
using System;
using System.Collections.Generic;

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

	//Creates a NodeLibrary and makes it active, so you can create / serialize ActionGraph instances in tests.
	public static IDisposable PushNodeLibrary()
	{
		var oldNodeLib = Game.NodeLibrary;

		var nodeLib = Game.NodeLibrary = new NodeLibrary( new TypeLoader( () => Game.TypeLibrary ) );

		nodeLib.AddAssembly( typeof( LogNodes ).Assembly );
		nodeLib.AddAssembly( typeof( Scene ).Assembly );

		return new DisposeAction( () =>
		{
			Game.NodeLibrary = oldNodeLib;
		} );
	}

	//Creates a SceneFile with the given resourcePath, populates it with game objects as given by gameObjectsJson, then loads it with Scene.Load.
	public static Scene LoadSceneFromJson( string resourcePath, params string[] gameObjectsJson )
	{
		var scene = new Scene();

		using var _ = scene.Push();

		var sceneFile = new SceneFile { GameObjects = gameObjectsJson.Select( Json.ParseToJsonObject ).ToArray() };

		sceneFile.RegisterWeakResourceId( resourcePath );

		var options = new SceneLoadOptions();
		options.SetScene( sceneFile );

		scene.Load( options );

		return scene;
	}

	//Gets the resource path of the action graph that implements the given delegate.
	public static string? GetSourcePath( this Delegate @delegate )
	{
		return (@delegate.GetActionGraphInstance()?.Graph.SourceLocation as GameResourceSourceLocation)?.Resource.ResourcePath;
	}

	//A NetworkSystem with a TestConnection so that messages broadcast from a host can
	//be inspected. Dispose at the end of the test to clean up this network system.
	public record TestNetworkSystem( NetworkSystem Server, TestConnection Connection ) : IDisposable
	{
		//Counts messages broadcast by the server with the given packed content type T.
		public int GetMessageCount<T>() => Connection.Messages.Count( x => x.Payload is T );

		//Dispose at the end of the test to clean up this network system.
		public void Dispose()
		{
			Server.GameSystem.Dispose();

			Networking.System = null;
		}
	}

	//Creates and initializes a NetworkSystem with a TestConnection so that messages broadcast from a host can be inspected.
	public static TestNetworkSystem InitializeHostWithTestConnection()
	{
		var server = new NetworkSystem( "server", GlobalGameNamespace.TypeLibrary );

		Networking.System = server;

		server.InitializeHost();
		server.GameSystem = new SceneNetworkSystem( GlobalGameNamespace.TypeLibrary, server );

		var testConnection = new TestConnection();

		server.OnConnected( testConnection );
		server.AddConnection( testConnection, new UserInfo { UserData = new Dictionary<string, string>() } );

		testConnection.State = Connection.ChannelState.Connected;

		return new TestNetworkSystem( server, testConnection );
	}
}
