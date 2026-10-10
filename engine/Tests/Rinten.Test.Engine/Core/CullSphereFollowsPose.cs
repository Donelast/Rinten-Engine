using System;
using System.Runtime.InteropServices;

namespace EngineTests.Core;

//Skinned models are culled against their current pose, not where it was when the transform moved
[TestClass]
[DoNotParallelize]
public class CullSphereFollowsPoseTests
{
	[StructLayout( LayoutKind.Sequential )]
	private struct Corner
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
	}

	static Model Triangle()
	{
		var corners = new[]
		{
			new Corner { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new Corner { Position = new Vector3( 1, 0, 0 ), Normal = Vector3.Up },
			new Corner { Position = new Vector3( 1, 1, 0 ), Normal = Vector3.Up },
		};

		var mesh = new Mesh();
		mesh.CreateVertexBuffer( corners.Length, corners.AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		return new ModelBuilder().AddMesh( mesh ).Create();
	}

	//A moved skinned renderer must mark its cull sphere stale, or it stays culled off screen
	[TestMethod]
	public void AMovedSkinnedRendererAsksToBeCulledAgain()
	{
		ThreadSafe.MarkMainThread();

		var model = Triangle();
		Assert.IsNotNull( model );

		var scene = new Scene();
		using var scope = scene.Push();

		var thing = scene.CreateObject();
		thing.WorldPosition = new Vector3( 0, 0, 500 );

		var renderer = thing.Components.Create<SkinnedModelRenderer>();
		renderer.Model = model;

		//Stand it in the drawer's array the way a first draw would.
		scene.GameTick( 1.0 / 60.0 );
		renderer.CullSlot = 0;
		renderer.CullSettled();
		ModelRenderer.CullMoved.Clear();

		//Carried somewhere else entirely, the way falling would.
		thing.WorldPosition = new Vector3( 0, 0, -500 );

		Assert.IsTrue( ModelRenderer.CullMoved.Contains( renderer ),
			"a skinned renderer moved a thousand units and never told the drawer, so it stays culled " +
			"against where it started - which is a model that falls into view and is never drawn" );

		//And the sphere it now reports must be where it actually is.
		renderer.CullSphere( out var centre, out _ );

		Assert.IsTrue( centre.z < -400f,
			$"its own sphere is still back at {centre}, so even a refreshed array would be wrong" );

		ModelRenderer.CullMoved.Clear();
	}

	//An animating but stationary renderer must not land on the moved list
	[TestMethod]
	public void AnAnimatingRendererThatStandsStillIsNotTreatedAsMoved()
	{
		ThreadSafe.MarkMainThread();

		var model = Triangle();
		Assert.IsNotNull( model );

		var scene = new Scene();
		using var scope = scene.Push();

		var thing = scene.CreateObject();
		thing.WorldPosition = Vector3.Zero;

		var renderer = thing.Components.Create<SkinnedModelRenderer>();
		renderer.Model = model;

		scene.GameTick( 1.0 / 60.0 );
		renderer.CullSlot = 0;
		renderer.CullSettled();
		ModelRenderer.CullMoved.Clear();

		var stood = thing.WorldTransform;

		for ( var i = 0; i < 5; i++ )
			scene.GameTick( 1.0 / 60.0 );

		Assert.AreEqual( stood.Position, thing.WorldTransform.Position,
			"the object was meant to stand still - this is about the pose, not the transform" );

		Assert.IsFalse( ModelRenderer.CullMoved.Contains( renderer ),
			"an animating renderer that did not move was put on the moved list, which costs it a " +
			"velocity pass it cannot use and a gather it did not need to lose" );

		ModelRenderer.CullMoved.Clear();
	}

	//The sphere is cached only while animators offer no bounds; must flip once one does
	[TestMethod]
	public void ASphereIsKeptOnlyWhileNothingUnseenCanChangeIt()
	{
		ThreadSafe.MarkMainThread();

		var scene = new Scene();
		using var scope = scene.Push();

		var plain = scene.CreateObject().Components.Create<ModelRenderer>();

		Assert.IsTrue( plain.CullSphereHolds,
			"a model that only moves when its object moves should keep its sphere" );

		var thing = scene.CreateObject();
		var skinned = thing.Components.Create<SkinnedModelRenderer>();
		skinned.Model = Triangle();

		scene.GameTick( 1.0 / 60.0 );

		var animation = skinned.SceneModel?.Animation;

		Assert.IsNotNull( animation, "the renderer should have something animating it by now" );

		//The rule, stated against what the animator actually says rather than against a constant - so
		//this test follows the engine rather than having to be remembered when the engine changes.
		var keepsABox = animation.LocalBounds is not null || animation.WorldBounds is not null;

		Assert.AreEqual( !keepsABox, skinned.CullSphereHolds,
			keepsABox
				? "the animator keeps a box around the pose, so the sphere cannot be cached"
				: "the animator keeps no box, so the bounds are the model's own and the sphere can be cached - " +
				  "recomputing it per cull is work for a number that cannot have changed" );
	}
}
