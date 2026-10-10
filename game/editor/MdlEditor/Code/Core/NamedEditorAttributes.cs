//Each one carries the name its editor has always answered to - Validator reads those names.

namespace Editor.MdlEditor;

/// <summary>When applied to a string property, picks an animation clip compatible with the target skeleton.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class ModelClipAttribute : EditorAttribute
{
	public ModelClipAttribute() { Value = "model.clip"; }
}

/// <summary>When applied to a string property, picks a bone on the graph's target model.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AnimGraphBoneAttribute : EditorAttribute
{
	public AnimGraphBoneAttribute() { Value = "animgraph.bone"; }
}

/// <summary>When applied to a string property, picks a bone mask, or a bone to root one at.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AnimGraphBoneMaskAttribute : EditorAttribute
{
	public AnimGraphBoneMaskAttribute() { Value = "animgraph.bonemask"; }
}

/// <summary>When applied to a string property, picks one of an ID parameter's options.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AnimGraphOptionsAttribute : EditorAttribute
{
	public AnimGraphOptionsAttribute() { Value = "animgraph.options"; }
}

/// <summary>When applied to a string property, picks a parameter declared on the graph.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AnimGraphParameterNameAttribute : EditorAttribute
{
	public AnimGraphParameterNameAttribute() { Value = "AnimGraphParameterName"; }
}

/// <summary>When applied to a string property, picks one of the IK chains the target model names.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AnimGraphIkChainAttribute : EditorAttribute
{
	public AnimGraphIkChainAttribute() { Value = "animgraph.ikchain"; }
}

/// <summary>When applied to a string property, picks one of the look-at chains the target model names.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class AnimGraphLookChainAttribute : EditorAttribute
{
	public AnimGraphLookChainAttribute() { Value = "animgraph.lookchain"; }
}
