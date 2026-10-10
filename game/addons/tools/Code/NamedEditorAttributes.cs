//Each one carries the name its editor has always answered to, so code reading that name still works.

namespace Editor;

/// <summary>When applied to a box side enum, lays the sides out as a row of buttons.</summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public sealed class BoxSidesAttribute : EditorAttribute
{
	public BoxSidesAttribute() { Value = "horizontal-box-sides"; }
}
