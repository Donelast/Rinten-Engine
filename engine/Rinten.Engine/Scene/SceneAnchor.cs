using System.Text.Json.Serialization;

namespace Rinten;

/// <summary>A serializable position and rotation, in world space or local to a GameObject it follows.</summary>
[Expose]
public struct SceneAnchor
{
	/// <summary>Position - in world space when <see cref="Parent"/> is null, otherwise local to it.</summary>
	[KeyProperty] public Vector3 LocalPosition { get; set; }

	/// <summary>Rotation - in world space when <see cref="Parent"/> is null, otherwise local to it.</summary>
	public Rotation LocalRotation { get; set; }

	/// <summary>The object this anchor is attached to, or null for a fixed world-space anchor. When set, <see cref="LocalPosition"/> and <see cref="LocalRotation"/> are interpreted relative to it.</summary>
	public GameObject Parent { get; set; }

	/// <summary>True when this anchor is attached to a valid object.</summary>
	[JsonIgnore]
	public readonly bool IsAnchored => Parent.IsValid();

	/// <summary>Resolves this anchor to a world-space transform.</summary>
	public readonly Transform ToWorld()
	{
		//A never-assigned Rotation is a zero quaternion, not identity - treat it as identity.
		var rotation = LocalRotation == default ? Rotation.Identity : LocalRotation;

		var local = new Transform( LocalPosition, rotation );

		//A destroyed parent still resolves against its last transform - our local position would make no sense as a world position.
		return Parent is not null ? Parent.WorldTransform.ToWorld( local ) : local;
	}

	/// <summary>The resolved world-space position.</summary>
	[JsonIgnore]
	public readonly Vector3 Position => ToWorld().Position;

	public static implicit operator SceneAnchor( Vector3 position ) => new() { LocalPosition = position };

	public static implicit operator SceneAnchor( Transform transform ) => new() { LocalPosition = transform.Position, LocalRotation = transform.Rotation };
}
