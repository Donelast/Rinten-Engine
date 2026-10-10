using System;

namespace Rinten;

//Per-bone boxes in bone space, for tracing skinned models in their current pose
internal sealed class ModelBoneBoxes
{
	private readonly BBox[] boxes;
	private readonly bool[] owned;

	/// <summary>Bone-space, so the box is put through the bone's transform.</summary>
	public int Count => boxes.Length;

	public bool Any { get; }

	private ModelBoneBoxes( BBox[] boxes, bool[] owned, bool any )
	{
		this.boxes = boxes;
		this.owned = owned;

		Any = any;
	}

	/// <summary>Works the boxes out, or returns null where the model has no skeleton, no geometry, or no skin binding the two together.</summary>
	public static ModelBoneBoxes Build( Model model )
	{
		if ( model?.core is not { IsValid: true } core ) return null;

		var bones = model.BoneCount;
		if ( bones <= 0 ) return null;

		if ( !model.GetTriangles( out var positions, out var indices ) ) return null;

		var owners = core.CopyTraceBoneOwners( positions.Length );
		if ( owners is null ) return null;

		//Model-to-bone at rest, which is what puts a vertex into its bone's space. The runtime transform is the other half of the same pair
		//- the two together are the skinning the shader does, so a box worked out here lands exactly where the vertices in it are drawn.
		var inverseBind = core.CopyInverseBinds( bones );
		if ( inverseBind is null ) return null;

		var boxes = new BBox[bones];
		var owned = new bool[bones];

		//Only the vertices that are actually drawn - the triangle list has already had the hidden body groups and the far levels taken out of it.
		foreach ( var index in indices )
		{
			if ( (uint)index >= (uint)owners.Length ) continue;

			var bone = owners[index];
			if ( (uint)bone >= (uint)bones ) continue;

			var local = ToBone( inverseBind, bone, positions[index] );

			boxes[bone] = owned[bone] ? boxes[bone].AddPoint( local ) : new BBox( local, local );
			owned[bone] = true;
		}

		var any = false;

		for ( var i = 0; i < bones; i++ )
		{
			if ( !owned[i] ) continue;

			any = true;

			//A bone carrying a flat sheet of vertices has a box with no thickness, which a ray can
			//only hit exactly edge on. A millimetre either way costs nothing and makes it pickable.
			boxes[i] = boxes[i].Grow( 0.001f );
		}

		return any ? new ModelBoneBoxes( boxes, owned, true ) : null;
	}

	//A point through one bone's inverse bind - row vectors, translation last.
	private static Vector3 ToBone( float[] matrices, int bone, in Vector3 point )
	{
		var at = bone * 16;

		return new Vector3(
			( point.x * matrices[at] ) + ( point.y * matrices[at + 4] ) + ( point.z * matrices[at + 8] ) + matrices[at + 12],
			( point.x * matrices[at + 1] ) + ( point.y * matrices[at + 5] ) + ( point.z * matrices[at + 9] ) + matrices[at + 13],
			( point.x * matrices[at + 2] ) + ( point.y * matrices[at + 6] ) + ( point.z * matrices[at + 10] ) + matrices[at + 14] );
	}

	//The fraction is along the segment given, so it is the same number every bone and every other object in the scene is measured by.
	/// <summary>Traces a segment against the bones as they stand, in world space.</summary>
	public bool Trace( ReadOnlySpan<Transform> bones, in Vector3 start, in Vector3 delta,
		out float fraction, out Vector3 normal, out int bone )
	{
		fraction = float.MaxValue;
		normal = default;
		bone = -1;

		var end = start + delta;

		var count = Math.Min( bones.Length, boxes.Length );

		for ( var i = 0; i < count; i++ )
		{
			if ( !owned[i] ) continue;

			var tx = bones[i];

			var localStart = tx.PointToLocal( start );
			var localDelta = tx.PointToLocal( end ) - localStart;

			if ( localDelta.IsNearZeroLength ) continue;

			if ( !Crosses( boxes[i], localStart, localDelta, out var t, out var face ) ) continue;

			if ( t >= fraction ) continue;

			fraction = t;
			normal = tx.NormalToWorld( face ).Normal;
			bone = i;
		}

		return bone >= 0;
	}

	//The slab test, with a segment that starts inside counting as a miss. A box a person is standing in is not
	//something they are pointing at, and reporting nought would put it in front of everything else in the scene.
	private static bool Crosses( in BBox box, in Vector3 start, in Vector3 delta,
		out float fraction, out Vector3 normal )
	{
		fraction = 0;
		normal = default;

		if ( box.Contains( start ) ) return false;

		var near = 0.0f;
		var far = 1.0f;

		var axis = -1;
		var sign = 0.0f;

		for ( var i = 0; i < 3; ++i )
		{
			var origin = start[i];
			var direction = delta[i];

			if ( MathF.Abs( direction ) < 1e-9f )
			{
				if ( origin < box.Mins[i] || origin > box.Maxs[i] ) return false;

				continue;
			}

			var inverse = 1.0f / direction;

			var first = ( box.Mins[i] - origin ) * inverse;
			var second = ( box.Maxs[i] - origin ) * inverse;

			var enterSign = -1.0f;

			if ( first > second )
			{
				(first, second) = (second, first);
				enterSign = 1.0f;
			}

			if ( first > near )
			{
				near = first;
				axis = i;
				sign = enterSign;
			}

			if ( second < far ) far = second;

			if ( near > far ) return false;
		}

		if ( axis < 0 ) return false;

		fraction = near;

		var face = Vector3.Zero;
		face[axis] = sign;
		normal = face;

		return true;
	}
}
