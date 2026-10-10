namespace Rinten.UI;

/// <summary>Mouse related <see cref="PanelEvent"/>.</summary>
public class MousePanelEvent : PanelEvent
{
	/// <summary>Position of the cursor relative to the panel's top left corner at the time the event was triggered.</summary>
	public Vector2 LocalPosition;

	/// <summary>Which button triggered the event, in string form.</summary>
	public new string Button;

	/// <summary>Which button triggered the event, as a <see cref="MouseButtons"/> enum.</summary>
	public MouseButtons MouseButton { get; set; }

	/// <summary>Pointer in root panel space when the button went down.</summary>
	public Vector2 PointerPosition;

	public MousePanelEvent( string event_name, Panel active, string button ) : base( event_name, active )
	{
		Name = event_name;
		Target = active;
		LocalPosition = Target.MousePosition + Target.ScrollOffset;

		if ( Target.FindRootPanel() is RootPanel root )
			PointerPosition = root.MousePos;

		Button = button;

		if ( button == "mouseleft" ) MouseButton = MouseButtons.Left;
		if ( button == "mouseright" ) MouseButton = MouseButtons.Right;
		if ( button == "mousemiddle" ) MouseButton = MouseButtons.Middle;
	}
}
