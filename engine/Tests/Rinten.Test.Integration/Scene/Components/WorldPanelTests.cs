using Rinten.Engine;
using Panel = Rinten.UI.Panel;
using Length = Rinten.UI.Length;
using PointerEvents = Rinten.UI.PointerEvents;

using Rinten.VR;

namespace SceneTests.Components;

[TestClass]
public class WorldPanelComponentTest
{
	//Enabling the WorldPanel component creates a world-space root panel (Rinten.UI.WorldPanel) at the GameObject's transform, marked
	//as a world panel that renders manually, with the default interaction range and the world panel's one-to-one panel scale.
	[TestMethod]
	public void EnableCreatesWorldSpacePanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 200, 300 );

		var wp = go.Components.Create<WorldPanel>();
		var panel = wp.GetPanel() as Rinten.UI.WorldPanel;

		Assert.IsNotNull( panel, "GetPanel should return the world space root panel" );
		Assert.IsTrue( panel.IsWorldPanel );
		Assert.IsTrue( panel.RenderedManually );
		Assert.AreEqual( go, panel.GameObject );
		Assert.IsTrue( panel.Position.Distance( go.WorldPosition ) < 0.001f );
		Assert.AreEqual( 1000.0f, panel.MaxInteractionDistance );
		Assert.AreEqual( 1.0f, panel.Scale, "a style pixel is a panel pixel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//The world panel's transform is re-copied from the GameObject during the pre-render
	//stage, so moving and rotating the GameObject moves the panel after a tick.
	[TestMethod]
	public void TransformFollowsGameObjectAfterTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		go.WorldPosition = new Vector3( 50, -20, 10 );
		go.WorldRotation = Rotation.From( 0, 90, 0 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.IsTrue( panel.Position.Distance( go.WorldPosition ) < 0.001f, "Panel should follow the GameObject position" );
		Assert.IsTrue( Vector3.Dot( panel.Rotation.Forward, go.WorldRotation.Forward ) > 0.999f, "Panel should follow the GameObject rotation" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//PanelSize and the alignment properties drive the panel bounds: the default Center/Center
	//alignment centers the rect around the origin, while Left/Top anchors it at the origin.
	[TestMethod]
	public void PanelSizeAndAlignmentDriveBounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		Assert.AreEqual( new Vector2( 512 ), wp.PanelSize );

		scene.GameTick();

		var b = panel.PanelBounds;
		Assert.AreEqual( -256f, b.Left );
		Assert.AreEqual( -256f, b.Top );
		Assert.AreEqual( 512f, b.Width );
		Assert.AreEqual( 512f, b.Height );

		wp.HorizontalAlign = WorldPanel.HAlignment.Left;
		wp.VerticalAlign = WorldPanel.VAlignment.Top;
		scene.GameTick();

		b = panel.PanelBounds;
		Assert.AreEqual( 0f, b.Left );
		Assert.AreEqual( 0f, b.Top );
		Assert.AreEqual( 512f, b.Width );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//RenderScale scales the world transform of the panel up while dividing the panel
	//bounds down, so the panel renders larger without changing its layout resolution.
	[TestMethod]
	public void RenderScaleScalesTransformAndBounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		wp.RenderScale = 2.0f;
		scene.GameTick();

		Assert.AreEqual( 2.0f, panel.WorldScale, 0.001f );

		var b = panel.PanelBounds;
		Assert.AreEqual( -128f, b.Left );
		Assert.AreEqual( -128f, b.Top );
		Assert.AreEqual( 256f, b.Width );
		Assert.AreEqual( 256f, b.Height );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//With LookAtCamera set the panel rotates to face the scene's main camera during pre-render instead of using the GameObject rotation.
	[TestMethod]
	public void LookAtCameraFacesMainCamera()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var camGo = scene.CreateObject();
		camGo.WorldPosition = new Vector3( 500, 200, 100 );
		camGo.Components.Create<CameraComponent>();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		wp.LookAtCamera = true;
		scene.GameTick();

		//The panel's face is its -Z, which is forward: facing the camera means forward points away from it.
		var away = (go.WorldPosition - camGo.WorldPosition).Normal;
		Assert.IsTrue( Vector3.Dot( panel.Rotation.Forward, away ) > 0.99f, "Panel should face the camera" );

		go.Destroy();
		camGo.Destroy();
		scene.ProcessDeletes();
	}

	//InteractionRange writes straight through to the live world panel's MaxInteractionDistance while the component is enabled.
	[TestMethod]
	public void InteractionRangePropagatesLive()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		Assert.AreEqual( 1000.0f, wp.InteractionRange );
		Assert.AreEqual( 1000.0f, panel.MaxInteractionDistance );

		wp.InteractionRange = 250.0f;
		Assert.AreEqual( 250.0f, panel.MaxInteractionDistance, "Setter should update the live panel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//GameObject tags are copied onto the world panel's scene object at enable, and tag changes made afterwards propagate through OnTagsChanged.
	[TestMethod]
	public void TagsPropagateToSceneObject()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Tags.Add( "hud" );

		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		Assert.IsTrue( panel.Tags.Has( "hud" ), "Tags present at enable should be copied" );

		go.Tags.Add( "extra" );
		scene.GameTick();

		Assert.IsTrue( panel.Tags.Has( "extra" ), "Tag changes should propagate to the world panel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//Disabling the component deletes the world panel immediately and nulls GetPanel. Re-enabling creates a brand new world panel instance.
	[TestMethod]
	public void DisableTearsDownPanel()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		var panel = (Rinten.UI.WorldPanel)wp.GetPanel();

		Assert.IsTrue( panel.IsValid );

		wp.Enabled = false;

		Assert.IsNull( wp.GetPanel(), "Disable should null the world panel" );
		Assert.IsFalse( panel.IsValid, "Disable should delete the world panel immediately" );

		wp.Enabled = true;

		var second = wp.GetPanel();
		Assert.IsNotNull( second );
		Assert.AreNotSame( panel, second, "Re-enable should create a new world panel" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//WorldPanel properties survive a serialize / deserialize round trip of the owning GameObject.
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wp = go.Components.Create<WorldPanel>();
		wp.PanelSize = new Vector2( 256, 128 );
		wp.RenderScale = 0.5f;
		wp.LookAtCamera = true;
		wp.HorizontalAlign = WorldPanel.HAlignment.Left;
		wp.VerticalAlign = WorldPanel.VAlignment.Bottom;
		wp.InteractionRange = 123.0f;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var wp2 = copy.GetComponent<WorldPanel>();
		Assert.IsNotNull( wp2 );
		Assert.AreEqual( new Vector2( 256, 128 ), wp2.PanelSize );
		Assert.AreEqual( 0.5f, wp2.RenderScale );
		Assert.IsTrue( wp2.LookAtCamera );
		Assert.AreEqual( WorldPanel.HAlignment.Left, wp2.HorizontalAlign );
		Assert.AreEqual( WorldPanel.VAlignment.Bottom, wp2.VerticalAlign );
		Assert.AreEqual( 123.0f, wp2.InteractionRange );

		go.Destroy();
		copy.Destroy();
		scene.ProcessDeletes();
	}

	//A world input whose ray lands on a panel two metres ahead hovers what it finds there
	//- the whole chain from the camera's ray through the panel's plane to the panel tree.
	[TestMethod]
	public void WorldInputHoversThePanelItPointsAt()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		Game.ActiveScene = scene;

		var camGo = scene.CreateObject();
		camGo.WorldPosition = new Vector3( 0, 1, 0 );
		camGo.Components.Create<CameraComponent>();
		var input = camGo.Components.Create<WorldInput>();

		var go = scene.CreateObject();
		go.WorldPosition = camGo.WorldPosition + camGo.WorldRotation.Forward * 2.0f;
		go.WorldRotation = Rotation.LookAt( go.WorldPosition - camGo.WorldPosition, Vector3.Up );

		var wp = go.Components.Create<WorldPanel>();
		var root = (Rinten.UI.WorldPanel)wp.GetPanel();

		var target = root.AddChild<Panel>( "target" );
		target.Style.Width = Length.Percent( 100 );
		target.Style.Height = Length.Percent( 100 );
		target.Style.PointerEvents = PointerEvents.All;

		//Headless, nothing renders, so the pre-render that copies the object's transform onto the panel never runs; put it there by hand.
		root.Transform = go.WorldTransform;

		//With the cursor hidden the input rays down the camera's own forward, which is what a player aiming with a crosshair gets.
		Mouse.Visibility = MouseVisibility.Hidden;

		for ( var i = 0; i < 3; i++ )
		{
			scene.GameTick();
			GlobalContext.Current.UISystem.Simulate( true );
		}

		Assert.IsTrue( root.RayToLocalPosition( new Ray( camGo.WorldPosition, camGo.WorldRotation.Forward ), out var local, out _ ), "the camera's ray should cross the panel" );
		Assert.IsTrue( local.Length < 1.0f, $"the ray should land on the panel's middle, not {local}" );
		Assert.IsTrue( root.ChildrenWantMouseInput, "a panel with pointer events wants the mouse" );

		Assert.AreEqual( target, input.Hovered, "the world input should hover the panel under its ray" );

		go.Destroy();
		camGo.Destroy();
		scene.ProcessDeletes();
	}
}

[TestClass]
public class WorldInputComponentTest
{
	//WorldInput exposes sensible defaults: Attack1/Attack2 as the click actions, the left
	//VR hand as input source, no hovered panel, and a live WorldPanelInput state object.
	[TestMethod]
	public void DefaultPropertySurface()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wi = go.Components.Create<WorldInput>();

		Assert.AreEqual( "Attack1", wi.LeftMouseAction );
		Assert.AreEqual( "Attack2", wi.RightMouseAction );
		Assert.AreEqual( VRHand.HandSources.Left, wi.VRHandSource );
		Assert.IsNull( wi.Hovered );
		Assert.IsNotNull( wi.WorldPanelInput );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//Every update the component aims its WorldPanelInput ray from the GameObject position along its
	//forward direction (no camera exists in this scene, so the mouse-cursor override path can't kick in).
	[TestMethod]
	public void UpdateAssignsRayFromTransform()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 10, 20, 30 );
		go.WorldRotation = Rotation.From( 10, 45, 0 );

		var wi = go.Components.Create<WorldInput>();

		scene.GameTick();

		var ray = wi.WorldPanelInput.Ray;
		Assert.IsTrue( ray.Position.Distance( go.WorldPosition ) < 0.001f, "Ray should start at the GameObject position" );
		Assert.IsTrue( Vector3.Dot( ray.Forward, go.WorldRotation.Forward ) > 0.999f, "Ray should point along the GameObject forward" );

		go.Destroy();
		scene.ProcessDeletes();
	}

	//WorldInput properties survive a serialize / deserialize round trip of the owning GameObject.
	[TestMethod]
	public void SerializationRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var wi = go.Components.Create<WorldInput>();
		wi.LeftMouseAction = "Use";
		wi.RightMouseAction = "Menu";
		wi.VRHandSource = VRHand.HandSources.Right;

		var json = go.Serialize().ToJsonString();
		var jsonObject = Json.ParseToJsonObject( json );
		Assert.IsNotNull( jsonObject );
		SceneUtility.MakeIdGuidsUnique( jsonObject );

		var copy = new GameObject( false );
		copy.Deserialize( jsonObject );
		copy.Enabled = true;

		var wi2 = copy.GetComponent<WorldInput>();
		Assert.IsNotNull( wi2 );
		Assert.AreEqual( "Use", wi2.LeftMouseAction );
		Assert.AreEqual( "Menu", wi2.RightMouseAction );
		Assert.AreEqual( VRHand.HandSources.Right, wi2.VRHandSource );

		go.Destroy();
		copy.Destroy();
		scene.ProcessDeletes();
	}
}
