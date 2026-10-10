namespace Editor;

/// <summary>One step of the first-run guide: what it says and the part of the editor it points at.</summary>
public sealed class WelcomeStep
{
	/// <summary>The message to display for this step</summary>
	public string Message { get; set; }

	/// <summary>Function to get the widget to highlight (if any)</summary>
	public Func<Widget> GetHighlightWidget { get; set; }

	/// <summary>Action to run when this step is shown (e.g., to prepare the UI)</summary>
	public Action OnEnter { get; set; }

	public WelcomeStep( string message, Func<Widget> getHighlightWidget = null, Action onEnter = null )
	{
		Message = message;
		GetHighlightWidget = getHighlightWidget;
		OnEnter = onEnter;
	}
}
