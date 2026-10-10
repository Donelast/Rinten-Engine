namespace Editor;

/// <summary>Something a toolbar button runs, and what it shows while it can be run.</summary>
public interface IToolbarCommand
{
	/// <summary>The icon it wears, by name.</summary>
	string Icon { get; }

	/// <summary>What the pointer says it does.</summary>
	string ToolTip { get; }

	/// <summary>Whether whatever it turns on is on.</summary>
	bool IsActive { get; }

	/// <summary>Whether it can be run at all.</summary>
	bool IsEnabled { get; }

	/// <summary>Runs it.</summary>
	void Execute();
}
