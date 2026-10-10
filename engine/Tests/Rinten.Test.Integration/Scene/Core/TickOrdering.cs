using System.Collections.Generic;

namespace SceneTests.Core;

//Pins the per-tick ordering contract: all fixed updates run before the frame update, and a paused game suppresses both.
[TestClass]
public class TickOrderingTest
{
	//Within one GameTick every fixed update runs before the frame update.
	[TestMethod]
	public void FixedUpdatesRunBeforeUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<TickProbe>();
		probe.Log = new List<string>();

		scene.GameTick( 0.1 );

		Assert.IsTrue( probe.Log.Contains( "fixed" ), $"no fixed update ran: {string.Join( ",", probe.Log )}" );
		Assert.IsTrue( probe.Log.Contains( "update" ), $"no update ran: {string.Join( ",", probe.Log )}" );

		var lastFixed = probe.Log.LastIndexOf( "fixed" );
		var firstUpdate = probe.Log.IndexOf( "update" );
		Assert.IsTrue( lastFixed < firstUpdate, $"update ran before fixed finished: {string.Join( ",", probe.Log )}" );
	}

	//While the game is paused, GameTick must not run updates or fixed updates - and they resume when unpaused.
	[TestMethod]
	public void PauseSuppressesUpdates()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var probe = go.Components.Create<TickProbe>();
		probe.Log = new List<string>();

		try
		{
			Game.IsPaused = true;
			scene.GameTick( 0.1 );

			Assert.AreEqual( 0, probe.Log.Count, $"updates ran while paused: {string.Join( ",", probe.Log )}" );

			Game.IsPaused = false;
			scene.GameTick( 0.1 );

			Assert.AreNotEqual( 0, probe.Log.Count );
		}
		finally
		{
			Game.IsPaused = false;
		}
	}
}

/// <summary>Logs the order of update callbacks. The subscriber interfaces are normally added by the game code generator - test components implement them by hand.</summary>
public class TickProbe : Component, Rinten.Internal.IUpdateSubscriber, Rinten.Internal.IFixedUpdateSubscriber
{
	public List<string> Log;

	protected override void OnUpdate() => Log?.Add( "update" );
	protected override void OnFixedUpdate() => Log?.Add( "fixed" );
}
