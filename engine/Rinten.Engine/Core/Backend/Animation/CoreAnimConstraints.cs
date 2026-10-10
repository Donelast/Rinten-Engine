using System.Collections.Generic;
using System.Numerics;

namespace Rinten.CoreAnimation;

//Procedural helper bones (twist, elbow helpers) solved on the final pose; drivers are averaged
internal static class CoreAnimConstraints
{
	//One constraint, with its bones already found in the skeleton.
	internal sealed class Bound
	{
		public ModelDefinition.ConstraintKind Kind;

		/// <summary>The bone it drives.</summary>
		public int Bone = -1;

		public float Weight = 1.0f;

		/// <summary>What it reads, as bones, and how much each says.</summary>
		public int[] Drivers = System.Array.Empty<int>();
		public float[] Shares = System.Array.Empty<float>();

		/// <summary>Where a driver sits on its bone, for the ones that read an attachment rather than a bone.</summary>
		public AnimTrs[] Offsets = System.Array.Empty<AnimTrs>();

		public int DriverAxis;
		public int BoneAxis;
	}

	//Which way an axis number points, in a bone's own space.
	private static Vector3 Axis( int which ) => which switch
	{
		1 => new Vector3( 0.0f, 1.0f, 0.0f ),
		2 => new Vector3( 0.0f, 0.0f, 1.0f ),
		_ => new Vector3( 1.0f, 0.0f, 0.0f ),
	};

	//Order is the model's and it matters: their own notes say so - a helper is put where its driver is first and turned back the right way up after, because the first constraint "completely screws the orientation up".
	/// <summary>Run a model's constraints over a pose, in the order the model lists them.</summary>
	public static void Apply( CoreAnimRuntime.Instance instance, int pose, IReadOnlyList<Bound> constraints )
	{
		if ( instance is null || constraints is null || constraints.Count == 0 ) return;

		var local = instance.Pose( pose );
		if ( local is null ) return;

		var skeleton = instance.Skeleton;

		foreach ( var constraint in constraints )
		{
			var bone = constraint.Bone;

			if ( bone < 0 || bone >= local.Length ) continue;
			if ( constraint.Drivers.Length == 0 ) continue;

			switch ( constraint.Kind )
			{
				case ModelDefinition.ConstraintKind.TiltTwist:
					Twist( local, skeleton, constraint );
					break;

				case ModelDefinition.ConstraintKind.Parent:
					Follow( local, skeleton, constraint );
					break;

				case ModelDefinition.ConstraintKind.Orient:
					Turn( local, skeleton, constraint );
					break;

				case ModelDefinition.ConstraintKind.Aim:
					Point( local, skeleton, constraint );
					break;
			}
		}
	}

	//Twist bone: takes a share of the driver's rotation about the bone's own axis, bending excluded
	private static void Twist( AnimTrs[] local, CoreAnimRuntime.Skeleton skeleton, Bound constraint )
	{
		//Several drivers are a weighted average of what each of them says, the same as everywhere else here. One is the usual answer.
		var angle = 0.0f;
		var whole = 0.0f;

		for ( var i = 0; i < constraint.Drivers.Length; i++ )
		{
			var driver = constraint.Drivers[i];

			if ( driver < 0 || driver >= local.Length ) continue;

			var share = i < constraint.Shares.Length ? constraint.Shares[i] : 1.0f;

			if ( share == 0.0f ) continue;

			var rest = skeleton.Bones[driver].Rest.Rotation;

			var turned = AnimTrs.NormaliseQuat( Quaternion.Inverse( AnimTrs.NormaliseQuat( rest ) )
				* AnimTrs.NormaliseQuat( local[driver].Rotation ) );

			angle += AnimTrs.TwistAngle( turned, Axis( constraint.DriverAxis ) ) * share;
			whole += share;
		}

		if ( whole <= 0.0f ) return;

		angle = angle / whole * constraint.Weight;

		var about = Axis( constraint.BoneAxis );

		var twist = Quaternion.CreateFromAxisAngle(
			new System.Numerics.Vector3( about.x, about.y, about.z ), angle );

		local[constraint.Bone].Rotation = AnimTrs.NormaliseQuat(
			AnimTrs.NormaliseQuat( skeleton.Bones[constraint.Bone].Rest.Rotation ) * twist );
	}

	//Moves the bone to its drivers' weighted average position; rotation intentionally untouched
	private static void Follow( AnimTrs[] local, CoreAnimRuntime.Skeleton skeleton, Bound constraint )
	{
		var wanted = Vector3.Zero;
		var whole = 0.0f;

		for ( var i = 0; i < constraint.Drivers.Length; i++ )
		{
			var driver = constraint.Drivers[i];

			if ( driver < 0 || driver >= local.Length ) continue;

			var share = i < constraint.Shares.Length ? constraint.Shares[i] : 1.0f;

			if ( share == 0.0f ) continue;

			var offset = i < constraint.Offsets.Length ? constraint.Offsets[i] : AnimTrs.Identity;

			var at = AnimTrs.Then( offset, CoreAnimRuntime.ModelOf( local, skeleton, driver ) );

			wanted += at.Position * share;
			whole += share;
		}

		if ( whole <= 0.0f ) return;

		wanted /= whole;

		var mine = CoreAnimRuntime.ModelOf( local, skeleton, constraint.Bone );

		var moved = ( wanted - mine.Position ) * constraint.Weight;

		if ( moved.Length < 1e-6f ) return;

		var parent = skeleton.Bones[constraint.Bone].Parent;

		var above = parent >= 0 ? CoreAnimRuntime.ModelOf( local, skeleton, parent ) : AnimTrs.Identity;

		mine.Position += moved;

		local[constraint.Bone].Position = AnimTrs.Then( mine, AnimTrs.Invert( above ) ).Position;
	}

	//Turn the way the driver turns, by weight. A weight of one is the driver's own turn and nothing of its own; the citizen's
	//elbow and knee helpers ask for half of it the other way, which is what turns them back after being dragged into place.
	private static void Turn( AnimTrs[] local, CoreAnimRuntime.Skeleton skeleton, Bound constraint )
	{
		var turned = Quaternion.Identity;
		var whole = 0.0f;

		for ( var i = 0; i < constraint.Drivers.Length; i++ )
		{
			var driver = constraint.Drivers[i];

			if ( driver < 0 || driver >= local.Length ) continue;

			var share = i < constraint.Shares.Length ? constraint.Shares[i] : 1.0f;

			if ( share == 0.0f ) continue;

			var rest = AnimTrs.NormaliseQuat( skeleton.Bones[driver].Rest.Rotation );

			var went = AnimTrs.NormaliseQuat( Quaternion.Inverse( rest )
				* AnimTrs.NormaliseQuat( local[driver].Rotation ) );

			whole += share;

			//Each one is worth its share of what is left, which walks the average round the turns rather than adding numbers that are not numbers.
			turned = AnimTrs.NormaliseQuat( Quaternion.Slerp( turned, went, share / whole ) );
		}

		if ( whole <= 0.0f ) return;

		var part = Quaternion.Slerp( Quaternion.Identity, turned, constraint.Weight );

		local[constraint.Bone].Rotation = AnimTrs.NormaliseQuat(
			AnimTrs.NormaliseQuat( skeleton.Bones[constraint.Bone].Rest.Rotation ) * part );
	}

	//Point the bone's length at the driver.
	private static void Point( AnimTrs[] local, CoreAnimRuntime.Skeleton skeleton, Bound constraint )
	{
		var parent = skeleton.Bones[constraint.Bone].Parent;

		var above = parent >= 0 ? CoreAnimRuntime.ModelOf( local, skeleton, parent ) : AnimTrs.Identity;

		var mine = CoreAnimRuntime.ModelOf( local, skeleton, constraint.Bone );

		var at = Vector3.Zero;
		var whole = 0.0f;

		for ( var i = 0; i < constraint.Drivers.Length; i++ )
		{
			var one = constraint.Drivers[i];

			if ( one < 0 || one >= local.Length ) continue;

			var much = i < constraint.Shares.Length ? constraint.Shares[i] : 1.0f;

			if ( much == 0.0f ) continue;

			var offset = i < constraint.Offsets.Length ? constraint.Offsets[i] : AnimTrs.Identity;

			at += AnimTrs.Then( offset, CoreAnimRuntime.ModelOf( local, skeleton, one ) ).Position * much;
			whole += much;
		}

		if ( whole <= 0.0f ) return;

		var wanted = ( at / whole ) - mine.Position;
		if ( wanted.Length < 1e-5f ) return;

		var along = (Rotation)AnimTrs.NormaliseQuat( mine.Rotation ) * Axis( constraint.BoneAxis );

		var turn = AnimTrs.Between( AnimTrs.NormaliseVec( along ), AnimTrs.NormaliseVec( wanted ) );

		var share = Quaternion.Slerp( Quaternion.Identity, turn, System.Math.Clamp( constraint.Weight, 0.0f, 1.0f ) );

		var turned = AnimTrs.NormaliseQuat( share * AnimTrs.NormaliseQuat( mine.Rotation ) );

		local[constraint.Bone].Rotation = AnimTrs.NormaliseQuat(
			Quaternion.Inverse( AnimTrs.NormaliseQuat( above.Rotation ) ) * turned );
	}
}
