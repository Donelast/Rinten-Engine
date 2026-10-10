namespace Rinten.UI;

/// <summary>Base <see cref="Panel"/> event.<br/> See <see cref="Panel.CreateEvent(PanelEvent)"/>.</summary>
public class PanelEvent
{
	public string Name { get; init; }
	public object Value { get; set; }
	public float Time { get; set; }
	public string Button { get; set; }

	/// <summary>The panel the event is currently being called on as it bubbles; Target is where it originated</summary>
	public Panel This { get; set; }
	public Panel Target { get; set; }

	internal bool Propagate = true;

	public PanelEvent( string eventName, Panel active = null )
	{
		Name = eventName;
		Target = active;
	}

	public bool Is( string name )
	{
		return string.Equals( name, Name, System.StringComparison.OrdinalIgnoreCase );
	}

	public void StopPropagation()
	{
		Propagate = false;
	}
}
