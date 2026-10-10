namespace Editor.NodeEditor;

/// <summary>Something in a graph a rubber band can catch and a delete can take away.</summary>
public interface IGraphSelectable : IValid
{
	/// <summary>Whether this is selected.</summary>
	bool Selected { get; set; }

	/// <summary>Whether it may be selected at all.</summary>
	bool Selectable { get; }

	/// <summary>What a rubber band has to touch to catch it, in graph units.</summary>
	Rect SelectionRect { get; }
}
