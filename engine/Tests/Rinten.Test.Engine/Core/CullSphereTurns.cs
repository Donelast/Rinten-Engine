using System;
using System.Runtime.InteropServices;

namespace EngineTests.Core;

//The cull sphere follows rotation of off-origin bounds, or objects vanish at screen edges
[TestClass]
[DoNotParallelize]
public class CullSphereTurnsTests
{
	//Enough of a vertex for a model to have bounds.
	[StructLayout( LayoutKind.Sequential )]
	private struct Corner
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
	}

	[TestMethod]
	public void TurningTheObjectTurnsItsSphere()
	{
		ThreadSafe.MarkMainThread();

		//A triangle two metres out along X, so its bounds are nowhere near the origin it will be turned about.
		var far = new[]
		{
			new Corner { Position = new Vector3( 2, 0, 0 ), Normal = Vector3.Up },
			new Corner { Position = new Vector3( 3, 0, 0 ), Normal = Vector3.Up },
			new Corner { Position = new Vector3( 3, 1, 0 ), Normal = Vector3.Up },
		};

		var mesh = new Mesh();

		mesh.CreateVertexBuffer( far.Length, far.AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		var model = new ModelBuilder().AddMesh( mesh ).Create();

		Assert.IsNotNull( model );

		var bounds = model.Bounds;

		Assert.IsTrue( bounds.Center.x > 1.5f,
			$"the model's own bounds should sit out along X, not at {bounds.Center}" );

		var scene = new Scene();
		using var scope = scene.Push();

		var thing = scene.CreateObject();
		thing.WorldPosition = Vector3.Zero;

		var renderer = thing.Components.Create<ModelRenderer>();
		renderer.Model = model;

		//Facing along X: the geometry is out at positive X and so is the sphere.
		thing.WorldRotation = Rotation.Identity;
		renderer.ForgetCullSphere();
		renderer.CullSphere( out var ahead, out var radius );

		Assert.IsTrue( radius > 0.4f, $"a sphere of nothing: {radius}" );
		Assert.IsTrue( ahead.x > 1.5f, $"unturned, the sphere should be out along X, not at {ahead}" );

		//Turned right round: the geometry is out at negative X, and the sphere has to have gone with it.
		thing.WorldRotation = Rotation.From( 0, 180, 0 );
		renderer.ForgetCullSphere();
		renderer.CullSphere( out var behind, out _ );

		Assert.IsTrue( behind.x < -1.5f,
			$"turned about, the sphere stayed at {behind} while the model went the other way - " +
			"the turn is not being applied to the bounds' centre" );
	}
}
