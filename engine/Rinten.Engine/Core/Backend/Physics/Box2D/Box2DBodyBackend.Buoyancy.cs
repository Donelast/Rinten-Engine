using Rinten.Native.Box2D;
using static Rinten.Native.Box2D.Box2D;

namespace Rinten;

//2D buoyancy: area below the fluid line pushes up at its centroid; bodies are 1m deep slabs
internal sealed unsafe partial class Box2DBodyBackend
{
	public void ApplyBuoyancyImpulse( Vector3 planeOrigin, Vector3 planeNormal, float fluidDensity,
		float linearDrag, float angularDrag, Vector3 fluidVelocity, Vector3 gravity, float dt )
	{
		if ( !IsValid || fluidDensity <= 0.0f || dt <= 0.0f ) return;
		if ( b2Body_GetType( id ) != b2BodyType.b2_dynamicBody ) return;

		var mass = b2Body_GetMass( id );
		if ( mass <= 0.0f ) return;

		var normal = new Vector2( planeNormal.x, planeNormal.y );
		if ( normal.LengthSquared < 1e-6f ) return;
		normal = normal.Normal;

		var origin = new Vector2( planeOrigin.x, planeOrigin.y );

		if ( !Submerged( normal, origin, out var totalArea, out var submergedArea, out var centre ) ) return;
		if ( totalArea <= 0.0f || submergedArea <= 0.0f ) return;

		var fraction = Math.Clamp( submergedArea / totalArea, 0.0f, 1.0f );

		//Never lift more than would float the body with less than this much of it under, so a very light body bobs instead of launching.
		const float minSubmergedFraction = 0.4f;
		var displaced = MathF.Min( fluidDensity * totalArea, mass / minSubmergedFraction );

		var scale = gravityEnabled ? gravityScale : 0.0f;
		var lift = -(displaced * fraction * scale * dt) * new Vector2( gravity.x, gravity.y );
		b2Body_ApplyLinearImpulse( id, Box2DPlane.Vec( lift ), Box2DPlane.Vec( centre ), true );

		var relative = Box2DPlane.Flat( b2Body_GetLinearVelocity( id ) ) - new Vector2( fluidVelocity.x, fluidVelocity.y );
		var size = MathF.Sqrt( totalArea );
		var sizeScale = 1.0f / MathF.Max( size, 0.01f );

		const float linearDragScale = 30.0f;
		var linear = Math.Clamp( linearDrag * linearDragScale * fraction * sizeScale * dt, 0.0f, 1.0f );
		if ( linear > 0.0f )
			b2Body_ApplyLinearImpulseToCenter( id, Box2DPlane.Vec( -linear * mass * relative ), true );

		const float angularDragScale = 15.0f;
		var angular = Math.Clamp( angularDrag * angularDragScale * fraction * dt, 0.0f, 1.0f );
		if ( angular > 0.0f )
			b2Body_ApplyAngularImpulse( id, -angular * b2Body_GetRotationalInertia( id ) * b2Body_GetAngularVelocity( id ), true );

		//Rising through the fluid is slowed more than sinking into it, or a body let go deep down leaves the surface like a cork out of a bottle.
		var g = new Vector2( gravity.x, gravity.y );
		if ( g.LengthSquared < 1e-12f ) return;

		var up = -g.Normal;
		var rise = Vector2.Dot( relative, up );
		if ( rise <= 0.0f ) return;

		const float quadraticDrag = 0.5f;
		var coefficient = quadraticDrag * fraction * sizeScale;
		var factor = 1.0f / (1.0f + coefficient * rise * dt) - 1.0f;
		b2Body_ApplyLinearImpulseToCenter( id, Box2DPlane.Vec( (mass * factor * rise) * up ), true );
	}

	private bool Submerged( Vector2 normal, Vector2 origin, out float totalArea, out float submergedArea, out Vector2 centre )
	{
		totalArea = 0.0f;
		submergedArea = 0.0f;
		centre = default;

		var transform = b2Body_GetTransform( id );

		Span<Vector2> outline = stackalloc Vector2[Box2DPart.MaxOutline];
		Span<Vector2> under = stackalloc Vector2[Box2DPart.MaxOutline * 2];

		var weighted = Vector2.Zero;
		var any = false;

		foreach ( var shape in shapes )
		{
			if ( shape?.Backend is not Box2DShapeBackend s ) continue;

			foreach ( var part in s.Parts )
			{
				var count = part.Outline( outline );
				if ( count < 3 ) continue;

				for ( var i = 0; i < count; i++ )
					outline[i] = Box2DPlane.Flat( Box2DPlane.Apply( transform, Box2DPlane.Vec( outline[i] ) ) );

				var (area, _) = AreaCentroid( outline[..count] );
				totalArea += MathF.Abs( area );
				any = true;

				var clipped = Clip( outline[..count], normal, origin, under );
				if ( clipped < 3 ) continue;

				var (underArea, underCentre) = AreaCentroid( under[..clipped] );
				underArea = MathF.Abs( underArea );
				if ( underArea <= 0.0f ) continue;

				submergedArea += underArea;
				weighted += underCentre * underArea;
			}
		}

		if ( submergedArea > 0.0f )
			centre = weighted * (1.0f / submergedArea);

		return any;
	}

	private static (float Area, Vector2 Centroid) AreaCentroid( ReadOnlySpan<Vector2> points )
	{
		var n = points.Length;
		if ( n < 3 ) return (0.0f, n > 0 ? points[0] : default);

		float area = 0, cx = 0, cy = 0;

		for ( var i = 0; i < n; i++ )
		{
			var a = points[i];
			var b = points[(i + 1) % n];
			var cross = a.x * b.y - b.x * a.y;
			area += cross;
			cx += (a.x + b.x) * cross;
			cy += (a.y + b.y) * cross;
		}

		area *= 0.5f;
		if ( MathF.Abs( area ) < 1e-9f ) return (0.0f, points[0]);

		var inv = 1.0f / (6.0f * area);
		return (area, new Vector2( cx * inv, cy * inv ));
	}

	//The part of an outline on the fluid's side of its surface line.
	private static int Clip( ReadOnlySpan<Vector2> points, Vector2 normal, Vector2 origin, Span<Vector2> output )
	{
		var n = points.Length;
		var count = 0;

		for ( var i = 0; i < n; i++ )
		{
			var current = points[i];
			var next = points[(i + 1) % n];

			var dc = Vector2.Dot( current - origin, normal );
			var dn = Vector2.Dot( next - origin, normal );

			var currentInside = dc <= 0.0f;
			var nextInside = dn <= 0.0f;

			if ( currentInside ) output[count++] = current;

			if ( currentInside != nextInside )
			{
				var t = dc / (dc - dn);
				output[count++] = current + (next - current) * t;
			}
		}

		return count;
	}
}
