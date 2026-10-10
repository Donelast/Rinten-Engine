namespace Editor;

/// <summary>How big the buttons of a group are drawn.</summary>
public enum ToolButtonSize
{
	/// <summary>A mode: the bigger button, 32 pixels, on the outer column.</summary>
	Mode,

	/// <summary>One of the smaller things a mode can do: 28 pixels, beside the modes.</summary>
	Option,

	/// <summary>One of the things the mode in hand can run on its pick: Option's size, two to a row, beside the move modes.</summary>
	Action,
}

/// <summary>A run of tool commands that belong together, drawn as one column beside the viewport.</summary>
public sealed class ToolGroup
{
	private readonly List<IToolCommand> commands = new();

	public ToolGroup( string name, ToolButtonSize size )
	{
		Name = name;
		Size = size;
	}

	/// <summary>What this group is, by name. Not drawn on the strip.</summary>
	public string Name { get; }

	/// <summary>How big its buttons are drawn.</summary>
	public ToolButtonSize Size { get; }

	/// <summary>The commands, in the order they go down the column.</summary>
	public IReadOnlyList<IToolCommand> Commands => commands;

	/// <summary>Whether there is nothing to draw.</summary>
	public bool IsEmpty => commands.Count == 0;

	/// <summary>Puts a command at the end of the column.</summary>
	public ToolGroup Add( IToolCommand command )
	{
		if ( command is not null ) commands.Add( command );

		return this;
	}

	/// <summary>Puts several at the end, in order.</summary>
	public ToolGroup AddRange( IEnumerable<IToolCommand> range )
	{
		foreach ( var command in range )
		{
			Add( command );
		}

		return this;
	}

	/// <summary>The command a shortcut belongs to, or null where none here has it.</summary>
	public IToolCommand Find( string shortcut )
	{
		if ( string.IsNullOrEmpty( shortcut ) ) return null;

		foreach ( var command in commands )
		{
			if ( command.Shortcut == shortcut ) return command;
		}

		return null;
	}

	/// <summary>A group with nothing in it, or null: what a tool answers when it has no column to show.</summary>
	public static ToolGroup OrNull( ToolGroup group ) => group is { IsEmpty: false } ? group : null;
}
