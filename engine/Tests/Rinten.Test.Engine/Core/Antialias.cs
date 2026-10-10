using System;

namespace EngineTests.Core;

//The compose pass's edge filter softens edges and leaves non-edges untouched
[TestClass]
[DoNotParallelize]
public class AntialiasTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void ItSoftensEdgesAndLeavesTheRestAlone()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core" ); return; }
		if ( !CoreRenderDevice.Initialize( validation: false ) ) { Assert.Inconclusive( "no device" ); return; }

		const int size = 256;

		var view = 0ul;
		var was = Rinten.Rendering.DrawSettings.Fxaa;

		try
		{
			//Pinned, so that two draws of a still scene are the same picture. The automatic one moves toward what it wants rather than
			//to it, and never quite arrives - which for a comparison of two captures reads as every pixel having changed a little.

			var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "no box or no pipelines" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 0.2f, -0.4f ).Normal );

			//Turned off the axes, so its edges cross the pixels diagonally - which is the only kind of edge that shows a difference.
			var box = scene.CreateObject();
			box.WorldPosition = new Vector3( 0, 0, -6 );
			box.WorldRotation = Rotation.From( 15, 20, 33 );
			box.WorldScale = 2.0f;
			box.Components.Create<ModelRenderer>().Model = model;

			var eye = scene.CreateObject();
			eye.WorldRotation = Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();
			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.1f;
			camera.ZFar = 100.0f;

			view = CoreRenderDevice.CreateView( size, size );
			Assert.AreNotEqual( 0ul, view );

			var drawer = new CoreSceneDrawer();

			var plain = new byte[size * size * 4];
			var control = new byte[size * size * 4];
			var filtered = new byte[size * size * 4];

			//Three captures: the middle unfiltered one measures exposure drift between frames
			Rinten.Rendering.DrawSettings.Fxaa = false;
			Render( drawer, scene, camera, view, size, 12, plain );
			Render( drawer, scene, camera, view, size, 12, control );

			Rinten.Rendering.DrawSettings.Fxaa = true;
			Render( drawer, scene, camera, view, size, 12, filtered );

			static int Difference( byte[] one, byte[] other, int at )
				=> Math.Max( Math.Abs( one[at] - other[at] ),
					Math.Max( Math.Abs( one[at + 1] - other[at + 1] ),
						Math.Abs( one[at + 2] - other[at + 2] ) ) );

			var changed = 0;
			var changedOnFlat = 0;
			var drifted = 0;
			var driftedOnFlat = 0;
			var worst = 0.0f;

			for ( var y = 1; y < size - 1; y++ )
			for ( var x = 1; x < size - 1; x++ )
			{
				var at = ( ( y * size ) + x ) * 4;

				var difference = Difference( plain, filtered, at );
				var drift = Difference( plain, control, at );

				var flat = IsFlat( plain, x, y, size );

				if ( drift > 2 )
				{
					drifted++;
					if ( flat ) driftedOnFlat++;
				}

				if ( difference <= 2 ) continue;

				changed++;

				if ( !flat ) continue;

				changedOnFlat++;
				worst = MathF.Max( worst, difference );
			}

			var big = 0;
			var huge = 0;
			var worstDrift = 0;
			var minX = size; var maxX = 0; var minY = size; var maxY = 0;

			for ( var y = 1; y < size - 1; y++ )
			for ( var x = 1; x < size - 1; x++ )
			{
				var drift = Difference( plain, control, ( ( y * size ) + x ) * 4 );

				if ( drift <= 2 ) continue;
				if ( drift > 8 ) big++;
				if ( drift > 32 ) huge++;

				worstDrift = Math.Max( worstDrift, drift );
				minX = Math.Min( minX, x ); maxX = Math.Max( maxX, x );
				minY = Math.Min( minY, y ); maxY = Math.Max( maxY, y );
			}

			Console.WriteLine( $"AA changed {changed} ({changedOnFlat} flat), " +
				$"the same picture twice changed {drifted} ({driftedOnFlat} flat) " +
				$"[>8 {big}, >32 {huge}, worst {worstDrift}, box {minX},{minY} to {maxX},{maxY}]" );

			Assert.IsTrue( changed > 100, $"the filter changed {changed} pixels - it is not running" );

			//It has to soften more than the picture wobbles by itself, and what it does away from edges has to be no more than that wobble.
			//The slack is generous on purpose: what is being ruled out is a filter that blurs everything, not one that is a few pixels untidy.
			Assert.IsTrue( changed > drifted * 2,
				$"the filter changed {changed} pixels and doing nothing changed {drifted}" );

			Assert.IsTrue( changedOnFlat <= ( driftedOnFlat * 2 ) + 200,
				$"{changedOnFlat} changed pixels are not on an edge, against {driftedOnFlat} " +
				$"that change on their own (worst {worst})" );
		}
		finally
		{
			Rinten.Rendering.DrawSettings.Fxaa = was;
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//Whether the filter would have found an edge here - the same neighbourhood and the same threshold it uses, in the light it uses them in.
	private static bool IsFlat( byte[] pixels, int x, int y, int size )
	{
		static float Linear( byte value )
		{
			var s = value / 255.0f;

			return s <= 0.04045f ? s / 12.92f : MathF.Pow( ( s + 0.055f ) / 1.055f, 2.4f );
		}

		float Luma( int px, int py )
		{
			var at = ( ( py * size ) + px ) * 4;

			return ( 0.299f * Linear( pixels[at] ) ) + ( 0.587f * Linear( pixels[at + 1] ) ) +
				( 0.114f * Linear( pixels[at + 2] ) );
		}

		var low = Luma( x, y );
		var high = low;

		for ( var dy = -1; dy <= 1; dy += 2 )
		for ( var dx = -1; dx <= 1; dx += 2 )
		{
			var corner = Luma( x + dx, y + dy );

			low = MathF.Min( low, corner );
			high = MathF.Max( high, corner );
		}

		return high - low < 1.0f / 24.0f;
	}

	private static void Render( CoreSceneDrawer drawer, Scene scene, CameraComponent camera,
		ulong view, int size, int frames, byte[] into )
	{
		for ( var frame = 0; frame < frames; frame++ )
		{
			if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) continue;

			try { drawer.Draw( scene, camera, view, size, size ); }
			finally { CoreRenderDevice.EndViewFrame( view ); }
		}

		Assert.IsTrue( CoreRenderDevice.ReadView( view, into ), "the picture should come back" );
	}
}
