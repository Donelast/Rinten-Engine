namespace Rinten;

/// <summary>A set of hitboxes on a model. Hitboxes can be boxes, spheres or capsules.</summary>
public class HitboxSet
{
	readonly List<Box> _all = new();

	//A set with nothing in it, for a model that describes none.
	internal HitboxSet() { }

	/// <summary>All hitboxes in this set</summary>
	public IReadOnlyList<Box> All => _all;

	/// <summary>A single hitbox on the model. This can be a box, sphere or capsule.</summary>
	public class Box
	{
		const int HITBOX_SHAPE_BOX = 0;
		const int HITBOX_SHAPE_SPHERE = 1;
		const int HITBOX_SHAPE_CAPSULE = 2;
		const int HITBOX_SHAPE_CYLINDER = 3;


		public string Name { get; init; }
		public string SurfaceName { get; init; }
		public BoneCollection.Bone Bone { get; init; }
		public ITagSet Tags { get; init; }

		/// <summary>Either a Sphere, Capsule or BBox</summary>
		public object Shape { get; init; }

		/// <summary>Get a random point inside this hitbox</summary>
		public Vector3 RandomPointInside
		{
			get
			{
				if ( Shape is Sphere sphere ) return sphere.RandomPointInside;
				if ( Shape is BBox bbox ) return bbox.RandomPointInside;
				if ( Shape is Capsule capsule ) return capsule.RandomPointInside;
				if ( Shape is Cone cone ) return cone.RandomPointInside;

				throw new NotImplementedException();
			}
		}

		/// <summary>Get a random point on the edge this hitbox</summary>
		public Vector3 RandomPointOnEdge
		{
			get
			{
				if ( Shape is Sphere sphere ) return sphere.RandomPointOnEdge;
				if ( Shape is BBox bbox ) return bbox.RandomPointOnEdge;
				if ( Shape is Capsule capsule ) return capsule.RandomPointOnEdge;
				if ( Shape is Cone cone ) return cone.RandomPointOnEdge;

				throw new NotImplementedException();
			}
		}
	}

	internal void Dispose()
	{
		_all.Clear();
	}
}

public partial class Model
{
	HitboxSet _hitboxset;

	/// <summary>Access to default hitbox set of this model</summary>
	public HitboxSet HitboxSet
	{
		get
		{
			if ( _hitboxset is null )
			{
				//Ours has no hitboxes to describe, and an empty set is what the callers already handle - they ask it for boxes and get none.
				_hitboxset = new HitboxSet();
			}

			return _hitboxset;
		}
	}

}
