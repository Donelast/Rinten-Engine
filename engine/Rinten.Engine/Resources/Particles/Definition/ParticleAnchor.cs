using System.Text.Json.Serialization;

namespace Rinten;

/// <summary>Which of the effect's anchors a property names.</summary>
[AttributeUsage( AttributeTargets.Property )]
public sealed class ParticleAnchorNameAttribute : Attribute { }

//This is what a "transform parameter" is: a shape can hang from one, a Goal can aim at one, and the player resolves each by name every frame - bound object first, then the player's override, then what is written here. See ParticlePlayer.Anchor.
/// <summary>A named pose the effect is built around - the hilt and the tip of a sword, the hand a spell is cast from, the thing a missile homes on. The file says where it sits by default, from the player; a scene binds it to a game object or a bone, and everything reading it follows.</summary>
[Expose]
public sealed class ParticleAnchor
{
	[Property]
	public string Name { get; set; } = "Anchor";

	[Hide]
	public string Identifier { get; set; } = Guid.NewGuid().ToString();

	/// <summary>Where it sits from the player, until a scene binds it somewhere.</summary>
	[Property]
	public Vector3 Offset { get; set; } = Vector3.Zero;

	[Property]
	public Angles Rotation { get; set; } = Angles.Zero;

	[Property]
	public Vector3 Scale { get; set; } = Vector3.One;

	[Hide, JsonIgnore]
	public Transform Local => new( Offset, Rotation.ToRotation(), Scale );
}

/// <summary>A scene's answer to an anchor: which object it is, and which bone of it. Listed on the player, so a sword's effect is dropped on a sword and told which two objects are the hilt and the tip.</summary>
[Expose]
public sealed class ParticleAnchorBinding
{
	[Property, ParticleAnchorName]
	public string Name { get; set; }

	[Property]
	public GameObject Target { get; set; }

	/// <summary>A bone of the target's skinned model, where it has one; empty is the object itself.</summary>
	[Property]
	public string Bone { get; set; }

	/// <summary>Kept from the bound thing on top of the file's own, in the target's space.</summary>
	[Property]
	public Vector3 Offset { get; set; } = Vector3.Zero;
}
