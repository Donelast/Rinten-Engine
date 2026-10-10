using Rinten.Internal;

namespace SceneTests.Components;

//OnUpdate/OnFixedUpdate run only with the subscriber marker; action properties work without it
[TestClass]
[DoNotParallelize]
public class ComponentUpdateTest : SceneTest
{
	//A component implementing IUpdateSubscriber gets exactly one OnUpdate call per game tick.
	[TestMethod]
	public void OnUpdateRunsOncePerGameTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<UpdateProbeComponent>();

		Assert.AreEqual( 0, comp.UpdateCalls );

		scene.GameTick();
		Assert.AreEqual( 1, comp.UpdateCalls );

		scene.GameTick();
		Assert.AreEqual( 2, comp.UpdateCalls );
	}

	//OnStart runs exactly once, and always before the first OnUpdate.
	[TestMethod]
	public void OnStartRunsOnceBeforeFirstUpdate()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<UpdateProbeComponent>();

		scene.GameTick();
		scene.GameTick();

		Assert.AreEqual( 1, comp.StartCalls );
		Assert.IsTrue( comp.StartedBeforeFirstUpdate );
	}

	//Disabling a component stops its OnUpdate from being called; re-enabling it resumes updates.
	[TestMethod]
	public void OnUpdateStopsWhenDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<UpdateProbeComponent>();

		scene.GameTick();
		Assert.AreEqual( 1, comp.UpdateCalls );

		comp.Enabled = false;
		scene.GameTick();
		Assert.AreEqual( 1, comp.UpdateCalls );

		comp.Enabled = true;
		scene.GameTick();
		Assert.AreEqual( 2, comp.UpdateCalls );
	}

	//A component implementing IFixedUpdateSubscriber gets OnFixedUpdate calls during the game tick, and stops getting them while disabled.
	[TestMethod]
	public void OnFixedUpdateRunsDuringGameTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<FixedUpdateProbeComponent>();

		Assert.AreEqual( 0, comp.FixedUpdateCalls );

		scene.GameTick();
		Assert.IsTrue( comp.FixedUpdateCalls > 0 );

		var countWhenDisabled = comp.FixedUpdateCalls;
		comp.Enabled = false;

		scene.GameTick();
		Assert.AreEqual( countWhenDisabled, comp.FixedUpdateCalls );
	}

	//Assigning OnComponentUpdate on a plain component (no IUpdateSubscriber) subscribes it to the update loop; clearing the action unsubscribes it.
	[TestMethod]
	public void OnComponentUpdateActionSubscribesPlainComponent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>();

		var calls = 0;
		comp.OnComponentUpdate = () => calls++;

		scene.GameTick();
		Assert.AreEqual( 1, calls );

		comp.OnComponentUpdate = null;

		scene.GameTick();
		Assert.AreEqual( 1, calls );
	}

	//An OnComponentUpdate action assigned while the component is disabled doesn't run, but starts running once the component is enabled.
	[TestMethod]
	public void OnComponentUpdateActionSetWhileDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>( false );

		var calls = 0;
		comp.OnComponentUpdate = () => calls++;

		scene.GameTick();
		Assert.AreEqual( 0, calls );

		comp.Enabled = true;

		scene.GameTick();
		Assert.AreEqual( 1, calls );
	}

	//The OnComponentStart action runs once on the first tick after the component becomes active, and never again.
	[TestMethod]
	public void OnComponentStartActionRunsOnce()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>();

		var calls = 0;
		comp.OnComponentStart = () => calls++;

		scene.GameTick();
		Assert.AreEqual( 1, calls );

		scene.GameTick();
		Assert.AreEqual( 1, calls );
	}

	//Assigning OnComponentFixedUpdate on a plain component subscribes it to the fixed update loop; clearing the action unsubscribes it.
	[TestMethod]
	public void OnComponentFixedUpdateActionSubscribesPlainComponent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<PlainTickComponent>();

		var calls = 0;
		comp.OnComponentFixedUpdate = () => calls++;

		scene.GameTick();
		Assert.IsTrue( calls > 0 );

		var countWhenCleared = calls;
		comp.OnComponentFixedUpdate = null;

		scene.GameTick();
		Assert.AreEqual( countWhenCleared, calls );
	}
}

/// <summary>Component subscribed to per-frame updates via the IUpdateSubscriber marker, recording OnStart/OnUpdate calls and their relative order.</summary>
public class UpdateProbeComponent : Component, IUpdateSubscriber
{
	public int StartCalls;
	public int UpdateCalls;
	public bool StartedBeforeFirstUpdate;

	//Counts OnStart invocations.
	protected override void OnStart()
	{
		StartCalls++;
	}

	//Counts OnUpdate invocations and records whether OnStart ran first.
	protected override void OnUpdate()
	{
		if ( UpdateCalls == 0 )
		{
			StartedBeforeFirstUpdate = StartCalls == 1;
		}

		UpdateCalls++;
	}
}

/// <summary>Component subscribed to fixed updates via the IFixedUpdateSubscriber marker.</summary>
public class FixedUpdateProbeComponent : Component, IFixedUpdateSubscriber
{
	public int FixedUpdateCalls;

	//Counts OnFixedUpdate invocations.
	protected override void OnFixedUpdate()
	{
		FixedUpdateCalls++;
	}
}

/// <summary>Component with no update overrides and no subscriber markers - it only takes part in the update loop via the OnComponent* action properties.</summary>
public class PlainTickComponent : Component
{
}
