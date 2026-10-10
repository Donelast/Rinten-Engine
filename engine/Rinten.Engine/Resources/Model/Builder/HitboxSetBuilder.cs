namespace Rinten;

/// <summary>Builds a named set of model hitboxes.</summary>
public sealed class HitboxSetBuilder
{
	/// <summary>The name of the hitbox set.</summary>
	public string Name { get; internal set; }

	internal List<ModelHitboxBuilder> Hitboxes { get; } = [];

	internal HitboxSetBuilder()
	{
	}

	public HitboxSetBuilder WithName( string name )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		Name = name;
		return this;
	}

	/// <summary>Adds a box hitbox in bone-local space.</summary>
	public ModelHitboxBuilder AddBox( string name, string boneName, BBox bounds )
	{
		return AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Box, bounds.Mins, bounds.Maxs, 0.0f );
	}

	/// <summary>Adds a sphere hitbox in bone-local space.</summary>
	public ModelHitboxBuilder AddSphere( string name, string boneName, Sphere sphere )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( sphere.Radius );
		return AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Sphere, sphere.Center, sphere.Center, sphere.Radius );
	}

	/// <summary>Adds a capsule hitbox in bone-local space.</summary>
	public ModelHitboxBuilder AddCapsule( string name, string boneName, Capsule capsule )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( capsule.Radius );
		return AddHitbox( name, boneName, ModelHitboxBuilder.ShapeType.Capsule, capsule.CenterA, capsule.CenterB, capsule.Radius );
	}

	private ModelHitboxBuilder AddHitbox( string name, string boneName, ModelHitboxBuilder.ShapeType shape, Vector3 min, Vector3 max, float radius )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentException.ThrowIfNullOrWhiteSpace( boneName );

		var builder = new ModelHitboxBuilder
		{
			Name = name,
			BoneName = boneName,
			Shape = shape,
			Min = min,
			Max = max,
			Radius = radius
		};

		Hitboxes.Add( builder );
		return builder;
	}
}
