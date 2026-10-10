//Each one carries the name its editor has always answered to, so code reading that name still works.

namespace Rinten;

/// <summary>When applied to a string property, picks one of the game's hold types.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class HoldTypeAttribute : EditorAttribute
{
	public HoldTypeAttribute() { Value = "holdtype"; }
}

/// <summary>When applied to a string property, picks an animation off the sprite beside it.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class SpriteAnimationNameAttribute : EditorAttribute
{
	public SpriteAnimationNameAttribute() { Value = "sprite_animation_name"; }
}

/// <summary>When applied to a string property, picks a folder on disk.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class FolderPathAttribute : EditorAttribute
{
	public FolderPathAttribute() { Value = "folder"; }
}

/// <summary>When applied to a string property, picks a sequence on the model beside it.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class SequenceNameAttribute : EditorAttribute
{
	public SequenceNameAttribute() { Value = "Sequence"; }
}

/// <summary>When applied to a clutter entry list, edits it as a grid rather than a column of rows.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class ClutterEntriesGridAttribute : EditorAttribute
{
	public ClutterEntriesGridAttribute() { Value = "ClutterEntriesGrid"; }
}

/// <summary>When applied to a string property, binds a key to it.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class KeyBindAttribute : EditorAttribute
{
	public KeyBindAttribute() { Value = "keybind"; }
}

/// <summary>When applied to a string property, edits it as a set of tags.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class TagStringAttribute : EditorAttribute
{
	public TagStringAttribute() { Value = "tags"; }
}

/// <summary>When applied to a string property, picks a bone off the model a definition compiles to.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class ModelDocBoneAttribute : EditorAttribute
{
	public ModelDocBoneAttribute() { Value = "model.bone"; }
}
