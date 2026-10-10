namespace Editor;

/// <summary>One button of a tool group: a mode a tool can be in, a shape it can draw, a way it can move what it picked.</summary>
public interface IToolCommand : IToolbarCommand
{
	/// <summary>What it is called - the first line of its tooltip.</summary>
	string Title { get; }

	/// <summary>The line under the title, or null for none.</summary>
	string Description { get; }

	/// <summary>The shortcut identifier that presses this, or null where it has no key.</summary>
	string Shortcut { get; }

	//For the one button that says two things: the shape tool wears a cube and a plus, one for what is drawn and one for the drawing.
	/// <summary>A second, smaller glyph over the corner of the icon, or null for none.</summary>
	string Badge { get; }
}
