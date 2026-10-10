using System;
using Facepunch.ActionGraphs;
using Rinten;
using Rinten.ActionGraphs;

namespace SceneTests.GameObjects;

//Scripts for the networking tests to put on a component. Its own file because building a graph wants
//the action graph namespace in scope, and that namespace has an attribute for half the words the scene tests already use.
internal static class NetworkScripts
{
	//A node library for as long as the caller needs one, and the old one back afterwards. Scoped to the one test that builds a graph rather than set up
	//for the whole class: standing this up in TestInitialize reads every node in the engine assembly for each test, and the run does not survive it.
	public static IDisposable NodeLibraryScope()
	{
		var previous = Game.NodeLibrary;

		Game.NodeLibrary = new NodeLibrary( new TypeLoader( () => Game.TypeLibrary ), new GraphLoader() );
		Game.NodeLibrary.AddAssembly( typeof( LogNodes ).Assembly );

		return new Restore( previous );
	}

	private sealed class Restore( NodeLibrary previous ) : IDisposable
	{
		public void Dispose() => Game.NodeLibrary = previous;
	}

	//The smallest script a component can carry: log a line and stop. What it does does not matter to the test - that it is a script does.
	public static Action<GameObject> HelloWorld()
	{
		var graph = ActionGraph.CreateDelegate<Action<GameObject>>( Game.NodeLibrary );

		var log = graph.Graph.AddNode( LogNodes.Info );
		log.Inputs.Signal.SetLink( graph.Graph.InputNode!.Outputs.Signal );
		log.Inputs["format"].Value = "Hello, World!";

		return graph;
	}
}
