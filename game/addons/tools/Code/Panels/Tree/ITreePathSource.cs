namespace Editor;

/// <summary>Tree host that exposes filesystem paths for multi-select drag.</summary>
public interface ITreePathSource
{
	/// <summary>Paths of the current selection.</summary>
	IReadOnlyList<string> GetSelectedPaths();
}
