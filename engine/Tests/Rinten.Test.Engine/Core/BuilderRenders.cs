using System;

namespace EngineTests.Core;

//A model built at runtime out of a vertex array is drawn where its vertices say - with the engine's own Vertex and with a vertex of two fields.
[TestClass]
[DoNotParallelize]
public class BuilderRendersTests
{
	private const int Size = 256;

	private struct Corner
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
	}

	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	private static int Lit( CameraComponent camera, CoreSceneDrawer drawer, Scene scene, ulong view )
	{
		for ( var frame = 0; frame < 6; frame++ )
		{
			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) );
			try { drawer.Draw( scene, camera, view, Size, Size ); }
			finally { Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) ); }
		}

		var pixels = new byte[Size * Size * 4];
		Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

		//The corner is background; whatever differs from it was drawn.
		var lit = 0;
		for ( var i = 0; i < Size * Size; i++ )
		{
			var at = i * 4;
			if ( Math.Abs( pixels[at] - pixels[0] ) > 8 || Math.Abs( pixels[at + 1] - pixels[1] ) > 8 || Math.Abs( pixels[at + 2] - pixels[2] ) > 8 ) lit++;
		}

		return lit;
	}

	[TestMethod]
	public void ABuiltQuadIsDrawn()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core library here" ); return; }
		if ( !CoreRenderDevice.Initialize( validation: false ) ) { Assert.Inconclusive( CoreRenderDevice.Description ); return; }

		var corners = new[]
		{
			new Corner { Position = new Vector3( 0, -2, -2 ), Normal = new Vector3( -1, 0, 0 ) },
			new Corner { Position = new Vector3( 0, 2, -2 ), Normal = new Vector3( -1, 0, 0 ) },
			new Corner { Position = new Vector3( 0, 2, 2 ), Normal = new Vector3( -1, 0, 0 ) },
			new Corner { Position = new Vector3( 0, -2, 2 ), Normal = new Vector3( -1, 0, 0 ) },
		};
		var plain = new[]
		{
			new Vertex( new Vector3( 0, -2, -2 ), new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector4( 0, 0, 0, 0 ) ),
			new Vertex( new Vector3( 0, 2, -2 ), new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector4( 1, 0, 0, 0 ) ),
			new Vertex( new Vector3( 0, 2, 2 ), new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector4( 1, 1, 0, 0 ) ),
			new Vertex( new Vector3( 0, -2, 2 ), new Vector3( -1, 0, 0 ), new Vector3( 0, 1, 0 ), new Vector4( 0, 1, 0, 0 ) ),
		};
		var indices = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };

		var a = new Mesh(); a.CreateVertexBuffer( corners.Length, corners.AsSpan() ); a.CreateIndexBuffer( indices.Length, indices );
		var b = new Mesh(); b.CreateVertexBuffer( plain.Length, plain.AsSpan() ); b.CreateIndexBuffer( indices.Length, indices );
		var twoFields = new ModelBuilder().AddMesh( a ).Create();
		var engineVertex = new ModelBuilder().AddMesh( b ).Create();
		var box = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

		var view = 0ul;
		CoreSceneDrawer drawer = null;

		try
		{
			var scene = new Scene();
			using var scope = scene.Push();
			scene.CreateObject().Components.Create<DirectionalLight>().WorldRotation = Rotation.LookAt( new Vector3( 1, 0.3f, -0.5f ).Normal );
			var eye = scene.CreateObject();
			var camera = eye.Components.Create<CameraComponent>();
			camera.BackgroundColor = new Color( 0.0f, 0.0f, 0.2f );
			camera.ZNear = 0.1f; camera.ZFar = 100.0f; camera.FieldOfView = 60.0f;
			eye.WorldPosition = new Vector3( -5, 0, 0 );

			if ( !CoreSceneDrawer.EnsureResources() ) { Assert.Inconclusive( CoreRenderDevice.Description ); return; }
			view = CoreRenderDevice.CreateView( Size, Size );
			drawer = new CoreSceneDrawer();

			var results = "";
			foreach ( var (name, model) in new[] { ( "two fields", twoFields ), ( "engine Vertex", engineVertex ), ( "dev box", box ) } )
			{
				var o = scene.CreateObject();
				o.Components.Create<ModelRenderer>().Model = model;
				if ( name == "dev box" ) o.WorldScale = new Vector3( 2, 2, 2 );
				var lit = Lit( camera, drawer, scene, view );
				results += $"{name}: {lit} of {Size * Size} pixels; ";
				o.Destroy();
			}

			Console.WriteLine( "BUILT " + results );
			Assert.IsTrue( results.Contains( "dev box: " ) && !results.Contains( "dev box: 0 " ), results );
			Assert.IsFalse( results.Contains( "engine Vertex: 0 " ), results );
			Assert.IsFalse( results.Contains( "two fields: 0 " ), results );
		}
		finally
		{
			drawer?.Release();
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );
		}
	}
}
