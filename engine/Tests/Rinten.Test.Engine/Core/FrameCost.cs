using System;
using System.Collections.Generic;

namespace EngineTests.Core;

//Frame cost benchmark, not a test (ends inconclusive); run by --filter, set up via RINTEN_* env vars
[TestClass]
[DoNotParallelize]
public class FrameCostTests
{
	[TestInitialize]
	public void OnMainThread() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void WhatTheWalkItselfCosts()
	{
		var scene = new Scene();
		using var scope = scene.Push();

		for ( var i = 0; i < 576; i++ )
			scene.CreateObject().Components.Create<ModelRenderer>();

		var list = new List<ModelRenderer>();

		var byIterator = double.MaxValue;
		var byList = double.MaxValue;
		var count = 0;

		for ( var round = 0; round < 200; round++ )
		{
			var start = System.Diagnostics.Stopwatch.GetTimestamp();

			for ( var pass = 0; pass < 10; pass++ )
				foreach ( var renderer in scene.GetAllComponents<ModelRenderer>() )
					if ( renderer.IsValid() ) count++;

			var took = Milliseconds( start ) / 10.0;
			if ( took < byIterator ) byIterator = took;

			start = System.Diagnostics.Stopwatch.GetTimestamp();

			for ( var pass = 0; pass < 10; pass++ )
			{
				list.Clear();
				scene.GetAll( list );

				for ( var i = 0; i < list.Count; i++ )
					if ( list[i].IsValid() ) count++;
			}

			took = Milliseconds( start ) / 10.0;
			if ( took < byList ) byList = took;
		}

		Console.WriteLine( $"WALK iterator {byIterator:F4} ms, list {byList:F4} ms, seen {count}" );

		//And the pieces of the loop body, which is what the walk was actually paying for.
		//Each timed on its own over the same five hundred and seventy-six renderers.
		var transforms = double.MaxValue;
		var overrides = double.MaxValue;

		list.Clear();
		scene.GetAll( list );

		for ( var round = 0; round < 200; round++ )
		{
			var start = System.Diagnostics.Stopwatch.GetTimestamp();
			for ( var i = 0; i < list.Count; i++ ) { var t = list[i].WorldTransform; count += t.Position.x > 0 ? 1 : 0; }
			var took = Milliseconds( start );
			if ( took < transforms ) transforms = took;

			start = System.Diagnostics.Stopwatch.GetTimestamp();
			for ( var i = 0; i < list.Count; i++ ) { var m = list[i].MaterialOverride; count += m is null ? 0 : 1; }
			took = Milliseconds( start );
			if ( took < overrides ) overrides = took;
		}

		//The rest of what the walk asks every renderer for. All of them are generated properties, and a generated property is not free.
		var tints = double.MaxValue;
		var groups = double.MaxValue;
		var models = double.MaxValue;
		var ids = double.MaxValue;

		for ( var round = 0; round < 200; round++ )
		{
			var start = System.Diagnostics.Stopwatch.GetTimestamp();
			for ( var i = 0; i < list.Count; i++ ) { var t = list[i].Tint; count += t.r > 0 ? 1 : 0; }
			var took = Milliseconds( start );
			if ( took < tints ) tints = took;

			start = System.Diagnostics.Stopwatch.GetTimestamp();
			for ( var i = 0; i < list.Count; i++ ) { var g = list[i].BodyGroups; count += g != 0 ? 1 : 0; }
			took = Milliseconds( start );
			if ( took < groups ) groups = took;

			start = System.Diagnostics.Stopwatch.GetTimestamp();
			for ( var i = 0; i < list.Count; i++ ) { var m = list[i].Model; count += m is null ? 0 : 1; }
			took = Milliseconds( start );
			if ( took < models ) models = took;

			start = System.Diagnostics.Stopwatch.GetTimestamp();
			for ( var i = 0; i < list.Count; i++ ) { var id = list[i].GameObject.Id; count += id == default ? 0 : 1; }
			took = Milliseconds( start );
			if ( took < ids ) ids = took;
		}

		Console.WriteLine( $"over {list.Count} renderers: transforms {transforms:F4} ms, overrides {overrides:F4} ms, " +
			$"tint {tints:F4} ms, groups {groups:F4} ms, model {models:F4} ms, id {ids:F4} ms" );
	}

	private static double Milliseconds( long from )
		=> ( System.Diagnostics.Stopwatch.GetTimestamp() - from ) * 1000.0
			/ System.Diagnostics.Stopwatch.Frequency;

	[TestMethod]
	public void WhereAFrameGoes()
	{
		if ( !CoreNative.Available ) { Assert.Inconclusive( "no core" ); return; }
		if ( !CoreRenderDevice.Initialize( validation: false ) ) { Assert.Inconclusive( "no device" ); return; }

		var view = 0ul;

		try
		{
			var model = Model.FromCore( CoreModel.Load( "models/dev/box.vmdl" ) );

			if ( model is null || !CoreSceneDrawer.EnsureResources() )
			{
				Assert.Inconclusive( "no box or no pipelines" );
				return;
			}

			var scene = new Scene();
			using var scope = scene.Push();

			var sun = scene.CreateObject().Components.Create<DirectionalLight>();
			sun.Shadows = true;
			sun.WorldRotation = Rotation.LookAt( new Vector3( 1, 1, -1 ).Normal );

			var across = int.TryParse( Environment.GetEnvironmentVariable( "RINTEN_BOXES" ), out var n ) ? n : 24;
			var random = 1234u;

			//Kept where everything moves every frame, which is the case the drawer's memory of the last frame cannot help with:
			//it pays the comparison and the writing down and gets nothing back. What that costs is worth a number of its own.
			var moving = Environment.GetEnvironmentVariable( "RINTEN_MOVE" ) == "1";
			var movers = moving ? new List<GameObject>() : null;

			for ( var x = 0; x < across; x++ )
			for ( var y = 0; y < across; y++ )
			{
				var box = scene.CreateObject();

				random = ( random * 1664525u ) + 1013904223u;

				box.WorldPosition = new Vector3( 20 + ( ( random >> 16 ) % 8 ),
					( x - ( across / 2 ) ) * 2.0f, ( y - ( across / 2 ) ) * 2.0f );

				box.Components.Create<ModelRenderer>().Model = model;

				movers?.Add( box );
			}

			var eye = scene.CreateObject();
			eye.WorldPosition = new Vector3( -30, 0, 0 );

			//Away from all of it, when asked: what the walk costs for objects that are not on the screen
			//at all is what a spatial index would save, and it is not the same number as the walk itself.
			eye.WorldRotation = Environment.GetEnvironmentVariable( "RINTEN_AWAY" ) == "1"
				? Rotation.LookAt( Vector3.Backward )
				: Rotation.LookAt( Vector3.Forward );

			var camera = eye.Components.Create<CameraComponent>();

			//Occlusion and reflections are components on the camera, so a measurement of them has to ask
			//for them. The console settings that follow decide how good they are, not whether they happen.
			eye.Components.Create<AmbientOcclusion>();
			eye.Components.Create<ScreenSpaceReflections>();
			eye.Components.Create<MotionBlur>();
			camera.BackgroundColor = Color.Black;
			camera.ZNear = 0.1f;
			camera.ZFar = 500.0f;
			camera.FieldOfView = 90.0f;

			if ( Environment.GetEnvironmentVariable( "RINTEN_SSR" ) == "0" )
				Rinten.ScreenSpaceReflections.Reflections = false;

			Rinten.Rendering.DrawSettings.Occlusion =
				Environment.GetEnvironmentVariable( "RINTEN_OCCLUSION" ) != "0";

			if ( int.TryParse( Environment.GetEnvironmentVariable( "RINTEN_AO" ), out var ao ) )
				Rinten.AmbientOcclusion.UserQuality = ao;

			if ( int.TryParse( Environment.GetEnvironmentVariable( "RINTEN_SSR_STEPS" ), out var steps ) )
				Rinten.ScreenSpaceReflections.ReflectionSteps = steps;

			if ( int.TryParse( Environment.GetEnvironmentVariable( "RINTEN_AO_RES" ), out var resolution ) )
				Rinten.AmbientOcclusion.UserResolution = resolution;

			if ( Environment.GetEnvironmentVariable( "RINTEN_DOF" ) == "1" )
			{
				var focus = eye.Components.Create<DepthOfField>();

				focus.FocalDistance = 10.0f;
				focus.FocusRange = 8.0f;
				focus.BlurSize = 10.0f;
			}

			view = CoreRenderDevice.CreateView( 1920, 1080 );

			var drawer = new CoreSceneDrawer { Watched = true };

			var bestCpu = double.MaxValue;
			var bestGpu = double.MaxValue;
			var zones = "";

			for ( var frame = 0; frame < 30; frame++ )
			{
				if ( movers is not null )
				{
					//A hand's width, so they stay where they were roughly - what is being measured is that they moved at all.
					var step = ( frame % 2 ) == 0 ? 0.1f : -0.1f;

					foreach ( var mover in movers )
						mover.WorldPosition += new Vector3( 0, 0, step );
				}

				if ( !CoreRenderDevice.BeginViewFrame( view, camera.BackgroundColor ) ) continue;

				try { drawer.Draw( scene, camera, view, 1920, 1080 ); }
				finally { CoreRenderDevice.EndViewFrame( view ); }

				if ( CoreRenderDevice.ReadProfile( view ) is not { } read ) continue;
				if ( read.GpuMs <= 0 ) continue;

				if ( read.CpuMs < bestCpu ) bestCpu = read.CpuMs;

				if ( read.GpuMs < bestGpu )
				{
					bestGpu = read.GpuMs;

					zones = string.Join( ", ", System.Linq.Enumerable.Select( read.Zones,
						z => $"{z.Name} {z.GpuMs:F2}/{z.CpuMs:F2}" ) );
				}
			}

			Console.WriteLine( $"MEASURED cpu {bestCpu:F3} ms, gpu {bestGpu:F3} ms" );
			//Both halves of every zone, gpu/cpu: a pass that costs nothing on the card can
			//still be the most expensive thing in the frame, and the queue is exactly that.
			Console.WriteLine( $"ZONES {zones}" );
			Assert.Inconclusive( $"cpu {bestCpu:F3} gpu {bestGpu:F3}" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}
}
