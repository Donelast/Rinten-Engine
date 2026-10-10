using System.Text.Json.Serialization;

namespace Rinten.Volumes;

/// <summary>A generic way to represent volumes in a scene. If we all end up using this instead of defining our own version in everything, we can improve this and improve everything at the same time.</summary>
[Expose]
public struct SceneVolume
{
	public SceneVolume()
	{
	}

	public enum VolumeTypes
	{
		/// <summary>A sphere. It's like the earth. Or an eyeball.</summary>
		Sphere,

		/// <summary>A box, like a cube.</summary>
		Box,

		/// <summary>A capsule, like a pill or a hotdog.</summary>
		Capsule,

		/// <summary>A global, infinite, boundless volume.</summary>
		Infinite = 1000,
	}

	[JsonInclude]
	public VolumeTypes Type = VolumeTypes.Box;

	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Sphere )]
	public Sphere Sphere = new Sphere( 0, 10 );

	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Box )]
	//Two and a half metres a side: the hundred units this was, in metres.
	public BBox Box = BBox.FromPositionAndSize( 0, 2.54f );

	[JsonInclude]
	[ShowIf( "Type", VolumeTypes.Capsule ), InlineEditor]
	//Two and a half metres tall and a quarter of a metre across, as above.
	public Capsule Capsule = Capsule.FromHeightAndRadius( 2.54f, 0.254f );

	/// <summary>Draws an editable sphere/box gizmo, for adjusting the volume.</summary>
	public void DrawGizmos( bool withControls ) => DrawGizmos( withControls, out _ );

	/// <summary>Draws an editable sphere/box gizmo, for adjusting the volume. <paramref name="changed"/> is true if a control changed the volume this frame.</summary>
	public void DrawGizmos( bool withControls, out bool changed )
	{
		changed = false;

		if ( Type == VolumeTypes.Sphere )
		{
			if ( withControls )
			{
				changed |= Gizmo.Control.Sphere( "Volume", Sphere.Radius, out Sphere.Radius, Color.Yellow );
			}

			Gizmo.Draw.IgnoreDepth = false;
			Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
			Gizmo.Draw.LineSphere( Sphere );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
			Gizmo.Draw.LineSphere( Sphere );
		}

		if ( Type == VolumeTypes.Box )
		{
			Gizmo.Draw.IgnoreDepth = false;

			if ( withControls )
			{
				changed |= Gizmo.Control.BoundingBox( "Volume", Box, out Box );
			}

			Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
			Gizmo.Draw.LineBBox( Box );

			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
			Gizmo.Draw.LineBBox( Box );
		}

		if ( Type == VolumeTypes.Capsule )
		{
			if ( withControls )
			{
				changed |= Gizmo.Control.Capsule( "Volume", Capsule, out Capsule, Color.Yellow );
			}

			Gizmo.Draw.IgnoreDepth = false;
			Gizmo.Draw.Color = Gizmo.Colors.Blue.WithAlpha( 0.8f );
			Gizmo.Draw.LineCapsule( Capsule );
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.Color = Color.White.WithAlpha( 0.05f );
			Gizmo.Draw.LineCapsule( Capsule );
		}
	}

	/// <summary>Is this point within the volume</summary>
	public bool Test( in Transform volumeTransform, in Vector3 position )
	{
		if ( Type == VolumeTypes.Infinite ) return true;

		return Test( volumeTransform.PointToLocal( position ) );
	}

	//Box-volume overlap test in volume space; conservative, may report touching near corners
	internal bool Test( in Transform volumeTransform, in BBox worldBox )
	{
		if ( Type == VolumeTypes.Infinite ) return true;

		var local = default( BBox );
		var any = false;

		foreach ( var corner in worldBox.Corners )
		{
			var p = volumeTransform.PointToLocal( corner );
			local = any ? local.AddPoint( p ) : new BBox( p, p );
			any = true;
		}

		return Type switch
		{
			VolumeTypes.Box => Box.Overlaps( local ),
			VolumeTypes.Sphere => Sphere.Center.DistanceSquared( local.ClosestPoint( Sphere.Center ) ) <= Sphere.Radius * Sphere.Radius,
			VolumeTypes.Capsule => CapsuleReaches( local ),
			_ => false,
		};
	}

	//Does this sphere touch the volume anywhere. In the volume's own space, where a sphere under a scale that is not the same
	//every way would be an ellipsoid; it is taken as the sphere that holds that ellipsoid, the radius over the smallest scale.
	internal bool Test( in Transform volumeTransform, in Sphere worldSphere )
	{
		if ( Type == VolumeTypes.Infinite ) return true;

		var scale = volumeTransform.Scale;
		var smallest = MathF.Max( MathF.Min( MathF.Abs( scale.x ), MathF.Min( MathF.Abs( scale.y ), MathF.Abs( scale.z ) ) ), 1e-6f );

		var centre = volumeTransform.PointToLocal( worldSphere.Center );
		var radius = worldSphere.Radius / smallest;

		return Type switch
		{
			VolumeTypes.Box => centre.DistanceSquared( Box.ClosestPoint( centre ) ) <= radius * radius,
			VolumeTypes.Sphere => centre.Distance( Sphere.Center ) <= radius + Sphere.Radius,
			VolumeTypes.Capsule => ClosestOnCapsuleAxis( centre ).Distance( centre ) <= radius + Capsule.Radius,
			_ => false,
		};
	}

	//The point of the capsule's middle line nearest a point.
	private readonly Vector3 ClosestOnCapsuleAxis( Vector3 point )
	{
		var axis = Capsule.CenterB - Capsule.CenterA;
		var length = axis.LengthSquared;

		if ( length < 1e-12f ) return Capsule.CenterA;

		var t = ( Vector3.Dot( point - Capsule.CenterA, axis ) / length ).Clamp( 0.0f, 1.0f );

		return Capsule.CenterA + ( axis * t );
	}

	//Whether the capsule reaches a box: the nearest pair of points between its middle line and the box, found by
	//stepping from one to the other - which settles in a couple of steps, a line and a box both being convex.
	private readonly bool CapsuleReaches( in BBox box )
	{
		var onAxis = ClosestOnCapsuleAxis( box.Center );
		var onBox = box.ClosestPoint( onAxis );

		for ( var i = 0; i < 4; ++i )
		{
			onAxis = ClosestOnCapsuleAxis( onBox );
			onBox = box.ClosestPoint( onAxis );
		}

		return onAxis.DistanceSquared( onBox ) <= Capsule.Radius * Capsule.Radius;
	}

	/// <summary>Is this point within the (local space) volume</summary>
	public bool Test( in Vector3 position )
	{
		if ( Type == VolumeTypes.Infinite ) return true;

		if ( Type == VolumeTypes.Sphere )
		{
			return Sphere.Contains( position );
		}

		if ( Type == VolumeTypes.Box )
		{
			return Box.Contains( position );
		}

		if ( Type == VolumeTypes.Capsule )
		{
			return Capsule.Contains( position );
		}

		return false;
	}

	/// <summary>Get the actual amount of volume in this shape. This is useful if you want to make a system where you prioritize by volume size. Don't forget to multiply by scale!</summary>
	public float GetVolume()
	{
		if ( Type == VolumeTypes.Sphere )
		{
			return Sphere.Volume;
		}

		if ( Type == VolumeTypes.Box )
		{
			return Box.Volume;
		}

		if ( Type == VolumeTypes.Capsule )
		{
			return Capsule.Volume;
		}

		return 0.0f;
	}

	/// <summary>Calculates the shortest distance from the specified world position to the edge of this volume.</summary>
	public float GetEdgeDistance( in Transform worldTransform, in Vector3 worldPosition )
	{
		//A huge number to represent "infinity" in this context
		if ( Type == VolumeTypes.Infinite ) return float.MaxValue;

		if ( Type == VolumeTypes.Sphere )
		{
			var localPos = worldTransform.PointToLocal( worldPosition );
			return Sphere.GetEdgeDistance( localPos );
		}

		if ( Type == VolumeTypes.Box )
		{
			var localPos = worldTransform.PointToLocal( worldPosition );
			return Box.GetEdgeDistance( localPos );
		}

		if ( Type == VolumeTypes.Capsule )
		{
			var localPos = worldTransform.PointToLocal( worldPosition );
			return Capsule.GetEdgeDistance( localPos );
		}

		return 0.0f;
	}

	/// <summary>Returns the axis-aligned bounding box that encloses the current volume.</summary>
	public BBox GetBounds()
	{
		if ( Type == VolumeTypes.Sphere )
		{
			return BBox.FromPositionAndSize( Sphere.Center, Vector3.One * Sphere.Radius * 2 );
		}
		if ( Type == VolumeTypes.Box )
		{
			return Box;
		}
		if ( Type == VolumeTypes.Capsule )
		{
			return Capsule.Bounds;
		}
		if ( Type == VolumeTypes.Infinite )
		{
			return new BBox( new Vector3( float.MinValue ), new Vector3( float.MaxValue ) );
		}

		return BBox.FromPositionAndSize( 0 );
	}
}
