namespace Editor.NodeEditor;

/// <summary>Where the pointer is, in both of the spaces a graph cares about.</summary>
public readonly record struct GraphPointer( Vector2 View, Vector2 Scene );

//A press picks one of these and the view drives it until the button comes up. Panning, boxing a selection, moving nodes, pulling a wire out of a plug - each is the same three moments, and each is a class rather than another flag on the view.
/// <summary>One thing the pointer is in the middle of doing to a graph.</summary>
public interface IGraphInteraction
{
	/// <summary>The pointer has moved while still held.</summary>
	void Move( GraphPointer pointer );

	/// <summary>The button has come up.</summary>
	void Finish( GraphPointer pointer );

	/// <summary>Called instead of <see cref="Finish"/> when the graph gives up on it.</summary>
	void Cancel();
}
