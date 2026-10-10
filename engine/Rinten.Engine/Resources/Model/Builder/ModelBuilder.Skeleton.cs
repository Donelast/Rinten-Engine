namespace Rinten;

public sealed partial class ModelBuilder
{
	private readonly List<BuilderBone> bones = [];

	private readonly record struct BuilderBone( string Name, string ParentName, Vector3 Position, Rotation Rotation, float Radius, bool Attachment );

	/// <summary>A bone definition for use with <see cref="ModelBuilder"/>.</summary>
	public readonly record struct Bone( string Name, string ParentName, Vector3 Position, Rotation Rotation );

	/// <summary>Adds a bone to the skeleton.</summary>
	public void AddBone( Bone bone )
	{
		AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );
	}

	/// <summary>Adds multiple bones to the skeleton.</summary>
	public void AddBones( Bone[] bones )
	{
		if ( bones == null )
			return;

		foreach ( var bone in bones )
			AddBone( bone.Name, bone.Position, bone.Rotation, bone.ParentName );
	}

	/// <summary>Adds a bone to the skeleton.</summary>
	public ModelBuilder AddBone( string name, Vector3 position, Rotation rotation, string parentName = null )
	{
		return AddBone( name, position, rotation, parentName, false );
	}

	/// <summary>Adds a named attachment point to the skeleton.</summary>
	public ModelBuilder AddAttachment( string name, Vector3 position, Rotation rotation, string parentName = null )
	{
		return AddBone( name, position, rotation, parentName, true );
	}

	internal ModelBuilder AddBone( string name, Vector3 position, Rotation rotation, string parentName, bool attachment )
	{
		bones.Add( new BuilderBone(
			string.IsNullOrWhiteSpace( name ) ? string.Empty : name,
			string.IsNullOrWhiteSpace( parentName ) ? string.Empty : parentName,
			position,
			rotation,
			-1,
			attachment ) );

		return this;
	}
}
