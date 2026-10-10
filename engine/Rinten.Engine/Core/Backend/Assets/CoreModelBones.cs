using System.Collections.Generic;
using System.Numerics;

namespace Rinten;

//Bones of a model under construction, read from the builder so added bones find their parents
internal static class CoreModelBones
{
	/// <summary>Adds a bone - the same call the importers make of the builder.</summary>
	public static unsafe int Add( CoreAssets.ModelBuilder builder, string name, int parent, float* inverseBind )
		=> builder.AddBone( name, parent, inverseBind );

	/// <summary>Puts the bones a model definition adds on top of the skeleton it was built with.</summary>
	public static unsafe int Write( CoreAssets.ModelBuilder builder, IReadOnlyList<ModelDefinition.ExtraBone> bones )
	{
		if ( builder is null || bones is null || bones.Count == 0 ) return 0;

		var left = new List<ModelDefinition.ExtraBone>( bones.Count );

		foreach ( var bone in bones )
		{
			if ( bone is null || string.IsNullOrWhiteSpace( bone.Name ) ) continue;

			left.Add( bone );
		}

		var added = 0;

		while ( left.Count > 0 )
		{
			var before = left.Count;

			for ( var i = left.Count - 1; i >= 0; i-- )
			{
				var bone = left[i];

				//Already in the skeleton: their own tools put the same bone in when they compiled the
				//model, and a second one of the same name is a bone nothing can find by name unambiguously.
				if ( builder.FindBone( bone.Name ) >= 0 )
				{
					left.RemoveAt( i );
					continue;
				}

				var parent = -1;
				var parentInverse = Matrix4x4.Identity;

				if ( !string.IsNullOrWhiteSpace( bone.Parent ) )
				{
					parent = builder.FindBone( bone.Parent );

					//Not there yet - it may be another of these, added this round or the next.
					if ( parent < 0 ) continue;

					if ( TryGet( builder, bone.Parent, out var found ) ) parentInverse = found;
				}

				var turn = bone.Rotation.ToRotation();

				var local = Matrix4x4.CreateFromQuaternion( new Quaternion( turn.x, turn.y, turn.z, turn.w ) )
					* Matrix4x4.CreateTranslation( bone.Position.x, bone.Position.y, bone.Position.z );

				if ( !Matrix4x4.Invert( local, out var undone ) ) undone = Matrix4x4.Identity;

				//World is the offset applied and then the parent's, so undoing it is the parent's undone first and the offset's on top.
				var inverse = parentInverse * undone;

				var matrix = stackalloc float[16]
				{
					inverse.M11, inverse.M12, inverse.M13, inverse.M14,
					inverse.M21, inverse.M22, inverse.M23, inverse.M24,
					inverse.M31, inverse.M32, inverse.M33, inverse.M34,
					inverse.M41, inverse.M42, inverse.M43, inverse.M44,
				};

				if ( Add( builder, bone.Name, parent, matrix ) >= 0 ) added++;

				left.RemoveAt( i );
			}

			if ( left.Count == before ) break;
		}

		return added;
	}

	/// <summary>Where a bone already in a builder rests, in the model's own space.</summary>
	public static bool TryRest( CoreAssets.ModelBuilder builder, string name, out Transform rest )
	{
		rest = new Transform( Vector3.Zero, Rotation.Identity );

		if ( string.IsNullOrWhiteSpace( name ) ) return false;
		if ( !TryGet( builder, name, out var inverseBind ) ) return false;
		if ( !Matrix4x4.Invert( inverseBind, out var pose ) ) return false;
		if ( !Matrix4x4.Decompose( pose, out _, out var turn, out var offset ) ) return false;

		rest = new Transform(
			new Vector3( offset.X, offset.Y, offset.Z ),
			new Rotation( turn.X, turn.Y, turn.Z, turn.W ) );

		return true;
	}

	private static bool TryGet( CoreAssets.ModelBuilder builder, string name, out Matrix4x4 inverseBind )
	{
		inverseBind = Matrix4x4.Identity;

		var bone = builder.FindBone( name );
		if ( bone < 0 ) return false;

		var m = builder.BoneInverseBind( bone );
		if ( m.Length < 16 ) return false;

		inverseBind = new Matrix4x4(
			m[0], m[1], m[2], m[3],
			m[4], m[5], m[6], m[7],
			m[8], m[9], m[10], m[11],
			m[12], m[13], m[14], m[15] );

		return true;
	}
}
