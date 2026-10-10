namespace Rinten;

public sealed partial class ModelDefinition
{
	//Simulated in world space on the final pose each frame; each bone swings from its parent
	/// <summary>Bones that swing on a spring after the pose: hair, a tail, a strap, a pouch, anything soft that should lag behind the body and settle.</summary>
	[Property, Group( "Animation" )]
	public List<JiggleBone> JiggleBones { get; set; } = new();

	/// <summary>Spheres on the body that jiggle bones are kept out of - a leg, a back - so a swinging bone goes round the body rather than through it.</summary>
	[Property, Group( "Animation" )]
	public List<JiggleCollider> JiggleColliders { get; set; } = new();

	//What swings is the bone's tip: where its child sits, or a point along the bone where it has none. The spring pulls that tip back toward wherever the pose put it, and the bone is turned to point at where the tip is.
	/// <summary>One bone on a spring.</summary>
	public class JiggleBone
	{
		/// <summary>The bone that swings.</summary>
		[Property, ModelDocBone]
		public string Bone { get; set; } = "";

		/// <summary>A switched off one is left exactly as the pose has it.</summary>
		[Property, Hide]
		public bool Enabled { get; set; } = true;

		/// <summary>How fast it springs back, in swings a second. Low is heavy and slow, high is small and quick.</summary>
		[Property, Range( 0.1f, 20.0f )]
		public float Stiffness { get; set; } = 3.0f;

		/// <summary>How quickly a swing dies away. Nought swings for ever; one settles without overshooting at all.</summary>
		[Property, Range( 0.0f, 2.0f )]
		public float Damping { get; set; } = 0.3f;

		/// <summary>How much the world's gravity pulls it down - nought is none, one is all of it.</summary>
		[Property, Range( 0.0f, 2.0f )]
		public float Gravity { get; set; } = 0.0f;

		/// <summary>How much the body's own travel through the world swings it. One is all of it - it lags when the body is moved; nought is none - only what the animation does to it.</summary>
		[Property, Range( 0.0f, 1.0f ), Title( "World Motion" )]
		public float WorldMotion { get; set; } = 1.0f;

		/// <summary>How far from where the pose points it may swing, in degrees.</summary>
		[Property, Range( 0.0f, 180.0f ), Title( "Max Angle" )]
		public float MaxAngle { get; set; } = 45.0f;

		/// <summary>How much of the swing is laid over the pose. One is all of it.</summary>
		[Property, Range( 0.0f, 1.0f )]
		public float Weight { get; set; } = 1.0f;

		/// <summary>How long the swinging part is, in metres. Nought measures it: to the bone's child, or a tenth of a metre where it has none.</summary>
		[Property, Range( 0.0f, 2.0f )]
		public float Length { get; set; } = 0.0f;

		/// <summary>Which way the bone points in its own space. Auto points it at its child, which is right for any bone that has one.</summary>
		[Property]
		public JiggleAxis Axis { get; set; } = JiggleAxis.Auto;

		/// <summary>How thick the tip is, in metres, for keeping it out of the colliders.</summary>
		[Property, Range( 0.0f, 0.5f )]
		public float Radius { get; set; } = 0.02f;

		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		/// <summary>What the model tree calls this row.</summary>
		[System.Text.Json.Serialization.JsonIgnore, Hide]
		public string Title => string.IsNullOrWhiteSpace( Bone ) ? "no bone" : Bone;
	}

	/// <summary>A sphere on a bone that jiggle bones are kept out of.</summary>
	public class JiggleCollider
	{
		/// <summary>The bone it rides.</summary>
		[Property, ModelDocBone]
		public string Bone { get; set; } = "";

		/// <summary>A switched off one keeps nothing out.</summary>
		[Property, Hide]
		public bool Enabled { get; set; } = true;

		/// <summary>Where on the bone its centre is, in metres.</summary>
		[Property]
		public Vector3 Position { get; set; }

		/// <summary>How big it is, in metres.</summary>
		[Property, Range( 0.0f, 1.0f )]
		public float Radius { get; set; } = 0.05f;

		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		/// <summary>What the model tree calls this row.</summary>
		[System.Text.Json.Serialization.JsonIgnore, Hide]
		public string Title
		{
			get
			{
				var name = string.IsNullOrWhiteSpace( Bone ) ? "no bone" : Bone;

				return $"{name}  ·  {Radius:0.###} m";
			}
		}
	}

	/// <summary>Which way a jiggle bone points, in its own space.</summary>
	public enum JiggleAxis
	{
		/// <summary>At its child, or along X where it has none.</summary>
		Auto,
		X,
		Y,
		Z,
		NegativeX,
		NegativeY,
		NegativeZ,
	}
}
