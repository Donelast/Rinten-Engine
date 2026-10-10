using System.Collections.Generic;

namespace Rinten;

//The list comes from the model this definition compiles to, plus whatever bones the definition adds itself - see the control in the tools (BoneNameControl).
/// <summary>Names a string property as a bone of the model being edited, so the editor offers the skeleton rather than a text box.</summary>
[System.AttributeUsage( System.AttributeTargets.Property | System.AttributeTargets.Field )]
public sealed class BoneNameAttribute : System.Attribute
{
}

public sealed partial class ModelDefinition
{
	//A bone plus offset, looked up by name in code (e.g. GetAttachment( "muzzle" ))
	/// <summary>Named places on the model that things are hung off: a muzzle, an eye, the hand a weapon sits in.</summary>
	[Property, Group( "Skeleton" )]
	public List<Attachment> Attachments { get; set; } = new();

	//Real compiled bones with no skin weights; also works on boneless models
	/// <summary>Bones this model has on top of the ones its skeleton came with.</summary>
	[Property, Group( "Skeleton" )]
	public List<ExtraBone> Bones { get; set; } = new();

	//Procedural helper bones (twist, corrective) computed from the final pose
	/// <summary>Bones another bone drives, worked out every frame rather than animated.</summary>
	[Property, Group( "Skeleton" )]
	public List<Constraint> Constraints { get; set; } = new();

	/// <summary>What one bone does about another.</summary>
	public class Constraint
	{
		/// <summary>Which of the kinds below.</summary>
		[Property]
		public ConstraintKind Kind { get; set; }

		/// <summary>Whether it is worked out at all, so one can be silenced to see what it was doing.</summary>
		[Property, Hide]
		public bool Enabled { get; set; } = true;

		/// <summary>The bone this moves.</summary>
		[Property, ModelDocBone, Title( "Driven Bone" )]
		public string Bone { get; set; } = "";

		/// <summary>How much of it, where the kind is a share of something.</summary>
		[Property, Title( "Amount" )]
		public float Weight { get; set; } = 1.0f;

		//Off the sheet on purpose: a driver is a node of its own under this one in the model tree, where it can be picked, dragged in the viewport and deleted like everything else. A list control here as well would be a second place to edit the same thing, agreeing with the first only until somebody uses it.
		/// <summary>What it is driven by, and how much each says.</summary>
		[Property, Hide]
		public List<Driver> Drivers { get; set; } = new();

		/// <summary>Which axis of the driver is read, and which of the slave is written - the bone's own length is X, which is what nearly all of them are.</summary>
		[Property, Title( "Driver Axis" ), ShowIf( nameof( ShowDriverAxis ), true )]
		public ConstraintAxis DriverAxis { get; set; }

		/// <inheritdoc cref="DriverAxis"/>
		[Property, Title( "Bone Axis" ), ShowIf( nameof( ShowBoneAxis ), true )]
		public ConstraintAxis BoneAxis { get; set; }

		//A name rather than a place in a tree: a node keeps its folder when the list around it is reordered, and a folder nobody is in stops existing on its own.
		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		/// <summary>Only a twist reads one axis of its driver and writes another.</summary>
		[Property, Hide, System.Text.Json.Serialization.JsonIgnore]
		public bool ShowDriverAxis => Kind == ConstraintKind.TiltTwist;

		/// <summary>A twist turns about an axis; an aim points one. The other two use neither.</summary>
		[Property, Hide, System.Text.Json.Serialization.JsonIgnore]
		public bool ShowBoneAxis => Kind is ConstraintKind.TiltTwist or ConstraintKind.Aim;

		/// <summary>What the model tree calls this row.</summary>
		[System.Text.Json.Serialization.JsonIgnore, Hide]
		public string Title
		{
			get
			{
				var name = string.IsNullOrWhiteSpace( Bone ) ? "no bone" : Bone;

				return $"{Named( Kind )}  ·  {name}";
			}
		}

		/// <summary>The kind in the words the editor offers it under.</summary>
		public static string Named( ConstraintKind kind ) => kind switch
		{
			ConstraintKind.TiltTwist => "Twist",
			ConstraintKind.Parent => "Parent",
			ConstraintKind.Orient => "Orient",
			ConstraintKind.Aim => "Aim",
			_ => kind.ToString(),
		};

		/// <summary>One thing a constraint listens to.</summary>
		public class Driver
		{
			/// <summary>A bone of the model, or empty where it is an attachment.</summary>
			[Property, ModelDocBone]
			public string Bone { get; set; } = "";

			//Only where the bone is not known. A place on a model is pulled between several bones by weight, so one is written out as a driver per bone it is pulled by and this is left empty; a name here is a definition written before that, and is looked up when the model is read.
			/// <summary>A place on the model, where it is not a bone.</summary>
			[Property, ShowIf( nameof( ShowAttachment ), true )]
			public string Attachment { get; set; } = "";

			/// <summary>Where on the bone it reads, in metres.</summary>
			[Property]
			public Vector3 Position { get; set; }

			/// <summary>Which way it faces there, relative to the bone.</summary>
			[Property]
			public Angles Rotation { get; set; }

			/// <summary>How much of the answer this one is. Several drivers on one constraint are a weighted average, not a list of things each done in turn.</summary>
			[Property, Title( "Share" )]
			public float Weight { get; set; } = 1.0f;

			/// <summary>An attachment is only read where no bone was named.</summary>
			[Property, Hide, System.Text.Json.Serialization.JsonIgnore]
			public bool ShowAttachment => string.IsNullOrWhiteSpace( Bone );

			/// <summary>The offset from the bone, as one thing.</summary>
			[System.Text.Json.Serialization.JsonIgnore, Hide]
			public Transform Local => new( Position, Rotation );

			/// <summary>What the model tree calls this row.</summary>
			[System.Text.Json.Serialization.JsonIgnore, Hide]
			public string Title
			{
				get
				{
					var name = !string.IsNullOrWhiteSpace( Bone ) ? Bone
						: !string.IsNullOrWhiteSpace( Attachment ) ? Attachment
						: "no bone";

					return Weight == 1.0f ? name : $"{name}  ·  {Weight:0.##}";
				}
			}
		}
	}

	//Written as a name and read as either: the ported models on disk were written when this was a number, and a number is still what Source's own document holds.
	/// <summary>Which way an axis points, in a bone's own space.</summary>
	public enum ConstraintAxis
	{
		X = 0,
		Y = 1,
		Z = 2,
	}

	/// <summary>The kinds of thing one bone does about another.</summary>
	public enum ConstraintKind
	{
		/// <summary>Take a share of how far the driver has twisted about itself.</summary>
		TiltTwist = 0,

		/// <summary>Sit where the drivers, averaged, sit - by weight, and past one, further than they went.</summary>
		Parent = 1,

		/// <summary>Turn the way the driver turns, by weight.</summary>
		Orient = 2,

		/// <summary>Point at the driver.</summary>
		Aim = 3,
	}

	/// <summary>A named place on the model, relative to a bone.</summary>
	public class Attachment
	{
		/// <summary>What code asks for it by.</summary>
		[Property]
		public string Name { get; set; } = "";

		/// <summary>Off is kept in the file and left out of the model.</summary>
		[Property, Hide]
		public bool Enabled { get; set; } = true;

		/// <summary>The bone it moves with. One of the model's own, or a bone this definition adds; empty hangs it off the model itself.</summary>
		[Property, BoneName, ModelDocBone]
		public string Parent { get; set; } = "";

		/// <summary>Where it sits, in metres, relative to that bone.</summary>
		[Property]
		public Vector3 Position { get; set; }

		/// <summary>Which way it points, relative to that bone.</summary>
		[Property]
		public Angles Rotation { get; set; }

		//Worked out from the two above, so it is not written into the file - a third copy of the same numbers in there is one that can disagree with them.
		/// <summary>The offset from the parent, as one thing.</summary>
		[System.Text.Json.Serialization.JsonIgnore, Hide]
		public Transform Local => new( Position, Rotation );

		//Follows its bones' position but keeps a fixed model-space rotation
		/// <summary>Whether it keeps facing one way whatever its bones do.</summary>
		[Property]
		public bool FacesOneWay { get; set; }

		//Weighted bones it sits between (e.g. mid-forearm); empty means it just follows Parent
		/// <summary>The bones it is pulled between, where it is not simply on one.</summary>
		[Property]
		public List<Influence> Influences { get; set; } = new();

		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		/// <summary>One bone a place is pulled by, and how much of it that bone is.</summary>
		public class Influence
		{
			[Property, BoneName, ModelDocBone]
			public string Bone { get; set; } = "";

			/// <summary>Where on that bone, in metres.</summary>
			[Property]
			public Vector3 Position { get; set; }

			/// <summary>Which way it faces there, relative to the bone.</summary>
			[Property]
			public Angles Rotation { get; set; }

			/// <summary>How much of the answer this bone is.</summary>
			[Property]
			public float Weight { get; set; } = 1.0f;

			/// <summary>The offset from the bone, as one thing.</summary>
			[System.Text.Json.Serialization.JsonIgnore, Hide]
			public Transform Local => new( Position, Rotation );
		}
	}

	/// <summary>A bone added on top of the skeleton the model came with.</summary>
	public class ExtraBone
	{
		/// <summary>What everything else names it by.</summary>
		[Property]
		public string Name { get; set; } = "";

		/// <summary>The bone it hangs off - one of the model's own, or another added one. Empty puts it on the model itself.</summary>
		[Property, BoneName]
		public string Parent { get; set; } = "";

		/// <summary>Where it sits, in metres, relative to its parent.</summary>
		[Property]
		public Vector3 Position { get; set; }

		/// <summary>Which way it points, relative to its parent.</summary>
		[Property]
		public Angles Rotation { get; set; }

		//Worked out from the two above, so it is not written into the file - a third copy of the same numbers in there is one that can disagree with them.
		/// <summary>The offset from the parent, as one thing.</summary>
		[System.Text.Json.Serialization.JsonIgnore, Hide]
		public Transform Local => new( Position, Rotation );

		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";
	}
}
