using System.Text.Json.Serialization;

namespace Rinten;

partial class ModelDefinition
{
	public enum JointKind
	{
		Hinge,
		Ball,
		Fixed,
		Slider,
	}

	//Anchor frame in parent bone space; twist is about its forward, swing cone aimed by SwingOffset
	/// <summary>A constraint between two hulls, matched by bone name at load.</summary>
	public class Joint
	{
		[Property]
		public string Name { get; set; } = "";

		[Property, Hide]
		public bool Enabled { get; set; } = true;

		[Property]
		public JointKind Type { get; set; } = JointKind.Ball;

		[Property, ModelDocBone, Title( "Parent Body" )]
		public string BoneA { get; set; } = "";

		[Property, ModelDocBone, Title( "Child Body" )]
		public string BoneB { get; set; } = "";

		/// <summary>Anchor in BoneA's space, or model space if <see cref="ModelSpace"/>.</summary>
		[Property, Title( "Anchor Origin" ), Description( "Where the joint turns about, in the parent bone's space." )]
		public Vector3 Position { get; set; }

		/// <summary>How the anchor frame is turned. Twist is measured about its forward.</summary>
		[Property, Title( "Anchor Angles" )]
		public Angles Rotation { get; set; }

		/// <summary>Ported anchors stay in model space until the editor places them.</summary>
		[Hide]
		public bool ModelSpace { get; set; }

		[Property, Title( "Collision Enabled" )]
		public bool EnableCollision { get; set; }

		//A name rather than a place in a tree: a node keeps its folder when the list around it is reordered, and a folder nobody is in stops existing on its own.
		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		/// <summary>The pull it takes to break this joint apart, and the twist it takes to snap it. Zero for either means it does not break that way.</summary>
		[Property, Title( "Linear Strength" ), ShowIf( nameof( ShowStrength ), true )]
		public float LinearStrength { get; set; }

		/// <inheritdoc cref="LinearStrength"/>
		[Property, Title( "Angular Strength" ), ShowIf( nameof( ShowStrength ), true )]
		public float AngularStrength { get; set; }

		[Property, Hide, JsonIgnore]
		public bool ShowStrength => Type != JointKind.Slider;

		//A ragdoll with no friction anywhere is a rag: every limb swings through its whole cone at the smallest push and keeps going. A little of this is the difference between a body that falls and one that flails.
		/// <summary>How much the joint resists being moved at all, 0 for one that swings free.</summary>
		[Property, Range( 0, 20 )]
		public float Friction { get; set; }

		[Property, Title( "Enable Swing Limit" ), ShowIf( nameof( Type ), JointKind.Ball )]
		public bool EnableSwingLimit { get; set; } = true;

		[Property, Hide, JsonIgnore]
		public bool ShowSwingLimit => Type == JointKind.Ball && EnableSwingLimit;

		/// <summary>How far the child may lean off the cone's axis. Degrees from the axis, so 20 is a 40-degree cone.</summary>
		[Property, Title( "Swing Limit" ), ShowIf( nameof( ShowSwingLimit ), true ), Range( 0, 180 )]
		public float SwingLimit { get; set; } = 30f;

		//This is what makes a limb hang where it actually hangs. Without it the only way to point a shoulder's cone down and out is to turn the whole anchor frame - which takes the twist axis with it, and then the twist limits are measured about something nobody meant.
		/// <summary>Aims the cone, turning it off the anchor frame's forward without moving the axis twist is measured about.</summary>
		[Property, Title( "Swing Offset Angle" ), ShowIf( nameof( ShowSwingLimit ), true )]
		public Angles SwingOffset { get; set; }

		[Property, Title( "Enable Twist Limit" ), ShowIf( nameof( Type ), JointKind.Ball )]
		public bool EnableTwistLimit { get; set; } = true;

		[Property, Hide, JsonIgnore]
		public bool ShowTwistLimit => Type == JointKind.Ball && EnableTwistLimit;

		[Property, Title( "Min Twist Angle" ), ShowIf( nameof( ShowTwistLimit ), true ), Range( -180, 180 )]
		public float MinTwist { get; set; } = -30f;

		[Property, Title( "Max Twist Angle" ), ShowIf( nameof( ShowTwistLimit ), true ), Range( -180, 180 )]
		public float MaxTwist { get; set; } = 30f;

		/// <summary>How stiffly the joint springs back toward its anchor frame, in hertz. Zero leaves it limp, which is what a ragdoll usually wants.</summary>
		[Property, Title( "Stiffness" ), ShowIf( nameof( Type ), JointKind.Ball ), Range( 0, 60 )]
		public float Stiffness { get; set; }

		/// <summary>How quickly that spring settles. Ignored while stiffness is zero.</summary>
		[Property, Title( "Damping" ), ShowIf( nameof( Type ), JointKind.Ball ), Range( 0, 5 )]
		public float Damping { get; set; } = 1f;

		[Property, ShowIf( nameof( Type ), JointKind.Hinge )]
		public float MinAngle { get; set; } = -90f;

		[Property, ShowIf( nameof( Type ), JointKind.Hinge )]
		public float MaxAngle { get; set; } = 90f;

		[Property, ShowIf( nameof( Type ), JointKind.Slider )]
		public float MinLength { get; set; }

		[Property, ShowIf( nameof( Type ), JointKind.Slider )]
		public float MaxLength { get; set; } = 0.2f;

		public string Title => string.IsNullOrWhiteSpace( Name )
			? $"{Type} {BoneA} → {BoneB}"
			: Name;
	}
}
