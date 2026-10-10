using Rinten.UI;

namespace Editor;

/// <summary>A ToolGroup as a button column; ignores the pointer except on its buttons</summary>
public sealed class ToolStripPanel : Panel
{
	private readonly List<ToolStripButton> buttons = new();

	public ToolStripPanel( ToolGroup group )
	{
		Group = group;

		AddClass( "tool-strip" );
		AddClass( group.Size == ToolButtonSize.Mode ? "modes" : "options" );
		if ( group.Size == ToolButtonSize.Action ) AddClass( "actions" );

		var labelled = group.Size == ToolButtonSize.Action;
		string section = null;

		foreach ( var command in group.Commands )
		{
			//A heading where the list moves on to another kind of command.
			if ( labelled && command is ToolCommand { Section: { Length: > 0 } next } && next != section )
			{
				section = next;
				AddChild<Rinten.UI.Label>( "heading" ).Text = next;
			}

			buttons.Add( AddChild( new ToolStripButton( command, labelled ) ) );
		}
	}

	/// <summary>The group this column draws.</summary>
	public ToolGroup Group { get; }

	/// <summary>The buttons, in the order they go down the column.</summary>
	public IReadOnlyList<ToolStripButton> Buttons => buttons;
}
