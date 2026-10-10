namespace Editor;

/// <summary>A widget that is nothing but a frame around a panel property editor.</summary>
public interface IPanelControlHost
{
	/// <summary>False when only the generic panel fallbacks answered, and the widget path should go on to its own generic editor rather than settle for this.</summary>
	bool HasPanelControl { get; }
}
