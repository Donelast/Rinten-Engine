namespace Editor.Inspectors;

/// <summary>A page that puts its own into the asset's first card rather than a first card of its own.</summary>
public interface IInspectorHead
{
	/// <summary>Move the page's buttons into <see cref="AssetHeaderPanel.Tools"/> and its settings into <see cref="AssetHeaderPanel.Fields"/>.</summary>
	void FillHead( AssetHeaderPanel head );
}
