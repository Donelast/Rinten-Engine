using System;

namespace Editor;

//What a flat view draws that a perspective one does not: the grid it is ruled with, and the outline of everything on it.
internal sealed class FlatViewPainter
{
	//How many pixels across a mesh has to be before its vertices are marked.
	private const float TicksAbovePixels = 24.0f;

	private readonly ViewportState state;
	private readonly Func<CameraComponent> camera;
	private readonly Func<Vector2> surfaceSize;

	public FlatViewPainter( ViewportState state, Func<CameraComponent> camera, Func<Vector2> surfaceSize )
	{
		this.state = state;
		this.camera = camera;
		this.surfaceSize = surfaceSize;
	}

	public void DrawGrid()
	{
		var active = camera();
		if ( !active.IsValid() ) return;

		var spacing = MathF.Max( Gizmo.Settings.GridSpacing, 0.001f );
		var height = MathF.Max( active.OrthographicHeight, 0.001f );
		var pixelSize = surfaceSize();
		var aspect = pixelSize.y > 0 ? pixelSize.x / pixelSize.y : 1f;
		var halfH = height * 0.5f;
		var halfW = halfH * aspect;
		var center = state.CameraPosition;
		var right = active.WorldRotation.Right;
		var up = active.WorldRotation.Up;

		Vector3 a = center + right * halfW + up * halfH;
		Vector3 b = center + right * halfW - up * halfH;
		Vector3 c = center - right * halfW + up * halfH;
		Vector3 d = center - right * halfW - up * halfH;

		GetGridAxes( state.GridAxis, out var axisU, out var axisV, out var colorU, out var colorV );

		float uMin = Min4( a.Dot( axisU ), b.Dot( axisU ), c.Dot( axisU ), d.Dot( axisU ) );
		float uMax = Max4( a.Dot( axisU ), b.Dot( axisU ), c.Dot( axisU ), d.Dot( axisU ) );
		float vMin = Min4( a.Dot( axisV ), b.Dot( axisV ), c.Dot( axisV ), d.Dot( axisV ) );
		float vMax = Max4( a.Dot( axisV ), b.Dot( axisV ), c.Dot( axisV ), d.Dot( axisV ) );

		const int maxLines = 160;
		while ( (uMax - uMin) / spacing > maxLines || (vMax - vMin) / spacing > maxLines )
			spacing *= 2f;

		var majorEvery = spacing * 8f;
		var opacity = MathF.Max( state.GridOpacity, 0.2f );
		var minor = new Color( 0.28f, 0.28f, 0.28f, opacity * 1.6f );
		var major = new Color( 0.42f, 0.42f, 0.42f, opacity * 2.2f );

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 1;

		DrawGridLines( axisU, axisV, uMin, uMax, vMin, vMax, spacing, majorEvery, minor, major, colorV );
		DrawGridLines( axisV, axisU, vMin, vMax, uMin, uMax, spacing, majorEvery, minor, major, colorU );
	}

	//The two axes a plane is spanned by, and the colour each is drawn in - by the letter, X red, Y green, Z blue, which is what every
	//other gizmo in the editor does. Colors.Left is gone: an axis colour named for a direction is what let the old mapping hide.
	private static void GetGridAxes( Gizmo.GridAxis axis, out Vector3 u, out Vector3 v, out Color colorU, out Color colorV )
	{
		switch ( axis )
		{
			case Gizmo.GridAxis.YZ:
				u = Vector3.Up;
				v = Vector3.Backward;
				colorU = Gizmo.Colors.Green;
				colorV = Gizmo.Colors.Blue;
				break;
			case Gizmo.GridAxis.ZX:
				u = Vector3.Backward;
				v = Vector3.Right;
				colorU = Gizmo.Colors.Blue;
				colorV = Gizmo.Colors.Red;
				break;
			default:
				u = Vector3.Right;
				v = Vector3.Up;
				colorU = Gizmo.Colors.Red;
				colorV = Gizmo.Colors.Green;
				break;
		}
	}

	private static void DrawGridLines( Vector3 lineAxis, Vector3 varyAxis, float lineMin, float lineMax,
		float varyMin, float varyMax, float spacing, float majorEvery, Color minor, Color major, Color axisColor )
	{
		var start = MathF.Floor( lineMin / spacing ) * spacing;
		var pad = spacing * 0.5f;

		for ( var t = start; t <= lineMax + pad; t += spacing )
		{
			if ( MathF.Abs( t ) <= spacing * 0.01f )
			{
				Gizmo.Draw.Color = axisColor.WithAlpha( 0.85f );
			}
			else if ( MathF.Abs( MathF.Round( t / majorEvery ) * majorEvery - t ) <= spacing * 0.01f )
			{
				Gizmo.Draw.Color = major;
			}
			else
			{
				Gizmo.Draw.Color = minor;
			}

			Gizmo.Draw.Line( lineAxis * t + varyAxis * varyMin, lineAxis * t + varyAxis * varyMax );
		}
	}

	private static float Min4( float a, float b, float c, float d ) => MathF.Min( MathF.Min( a, b ), MathF.Min( c, d ) );
	private static float Max4( float a, float b, float c, float d ) => MathF.Max( MathF.Max( a, b ), MathF.Max( c, d ) );

	//Flat-view culling, off with -noflatcull; read from raw args as the engine helper is internal
	private static readonly bool FlatCulling =
		!System.Environment.GetCommandLineArgs().Any(
			x => string.Equals( x, "-noflatcull", StringComparison.OrdinalIgnoreCase ) );

	//Whether an object's box is within the ortho view rectangle; depth is deliberately not tested
	private static bool OnFlatView( in BBox local, in Transform tx, in Vector3 centre,
		in Vector3 right, in Vector3 up, float halfW, float halfH )
	{
		return OnFlatView( local, tx, centre, right, up, halfW, halfH, out _, out _ );
	}

	//The same, and how big the thing is across the view - which decides whether what is drawn at each of its vertices can be told apart.
	private static bool OnFlatView( in BBox local, in Transform tx, in Vector3 centre,
		in Vector3 right, in Vector3 up, float halfW, float halfH,
		out float spanU, out float spanV )
	{
		var minU = float.MaxValue;
		var maxU = float.MinValue;
		var minV = float.MaxValue;
		var maxV = float.MinValue;

		for ( var i = 0; i < 8; i++ )
		{
			var corner = tx.PointToWorld( new Vector3(
				(i & 4) != 0 ? local.Mins.x : local.Maxs.x,
				(i & 2) != 0 ? local.Mins.y : local.Maxs.y,
				(i & 1) != 0 ? local.Mins.z : local.Maxs.z ) ) - centre;

			var u = corner.Dot( right );
			var v = corner.Dot( up );

			if ( u < minU ) minU = u;
			if ( u > maxU ) maxU = u;
			if ( v < minV ) minV = v;
			if ( v > maxV ) maxV = v;
		}

		//5% margin so objects don't blink out at the edges from small rectangle errors
		const float margin = 1.05f;

		spanU = maxU - minU;
		spanV = maxV - minV;

		return maxU >= -halfW * margin && minU <= halfW * margin &&
			maxV >= -halfH * margin && minV <= halfH * margin;
	}

	public void DrawMeshes( Scene scene )
	{
		var active = camera();
		if ( !scene.IsValid() || !active.IsValid() ) return;

		var size = surfaceSize();
		var wire = new Color( 0.35f, 0.72f, 1.0f );
		var pixel = size.y > 0 ? active.OrthographicHeight / size.y : 0.02f;
		var tick = MathF.Max( pixel * 4f, 0.004f );
		var right = active.WorldRotation.Right;
		var up = active.WorldRotation.Up;

		//What this flat view can show, in world units. Everything below is drawn edge by edge and vertex by vertex, per
		//view, per frame - so the cheapest thing that can be said about an object is whether it is on the screen at all.
		var centre = active.WorldPosition;
		var halfH = MathF.Max( active.OrthographicHeight, 0.001f ) * 0.5f;
		var aspect = size.y > 0 ? size.x / size.y : 1f;
		var halfW = halfH * aspect;

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Draw.LineThickness = 1;
		Gizmo.Draw.Color = wire;

		//Two loops, timed apart. Together they are the most expensive thing in an editor sitting still - 6.7 ms a frame across three flat views - and which of
		//them it is decides everything about what to do next: the mesh loop draws an edge and two ticks per vertex, the model loop draws one box per object.
		using ( Rinten.Diagnostics.Performance.Scope( "flat mesh loop" ) )
			foreach ( var mesh in scene.GetAllComponents<MeshComponent>() )
			{
				if ( !mesh.IsValid() || !mesh.Active || mesh.Mesh is null ) continue;
				if ( mesh.GameObject.Flags.HasFlag( GameObjectFlags.Hidden ) ) continue;
				if ( mesh.GameObject.Tags.Has( "skybox" ) ) continue;

				var spanU = float.MaxValue;
				var spanV = float.MaxValue;

				if ( mesh.Model is { } meshModel )
				{
					var on = OnFlatView( meshModel.Bounds, mesh.WorldTransform, centre, right, up,
						halfW, halfH, out spanU, out spanV );

					if ( FlatCulling && !on ) continue;
				}

				//Transform per view per frame; caching world positions was tried and was slower
				var tx = mesh.WorldTransform;

				foreach ( var edge in mesh.Mesh.GetEdges() )
					Gizmo.Draw.Line( tx.PointToWorld( edge.Start ), tx.PointToWorld( edge.End ) );

				//Skip vertex crosses on meshes too small on screen to tell them apart
				if ( MathF.Max( spanU, spanV ) < pixel * TicksAbovePixels ) continue;

				foreach ( var v in mesh.Mesh.GetVertexPositions() )
				{
					var w = tx.PointToWorld( v );
					Gizmo.Draw.Line( w - right * tick, w + right * tick );
					Gizmo.Draw.Line( w - up * tick, w + up * tick );
				}
			}

		using ( Rinten.Diagnostics.Performance.Scope( "flat model loop" ) )
			foreach ( var renderer in scene.GetAllComponents<ModelRenderer>() )
			{
				if ( !renderer.IsValid() || !renderer.Active || renderer.Model is null ) continue;
				if ( renderer.GameObject.Flags.HasFlag( GameObjectFlags.Hidden ) ) continue;
				if ( renderer.GameObject.Tags.Has( "skybox" ) ) continue;
				if ( renderer.GameObject.Components.Get<MeshComponent>() is not null ) continue;

				if ( FlatCulling &&
					!OnFlatView( renderer.Model.Bounds, renderer.WorldTransform, centre, right, up, halfW, halfH ) )
				{
					continue;
				}

				using ( Gizmo.ObjectScope( renderer.GameObject, renderer.WorldTransform ) )
				{
					Gizmo.Draw.LineBBox( renderer.Model.Bounds );
				}
			}
	}
}
