namespace Editor;

/// <summary>A command made of delegates, for callers with no type of their own to put one on.</summary>
public sealed class ToolbarCommand : IToolbarCommand
{
	private readonly Func<string> icon;
	private readonly Action run;

	public ToolbarCommand( string icon, string toolTip, Action run ) : this( () => icon, toolTip, run )
	{
	}

	public ToolbarCommand( Func<string> icon, string toolTip, Action run )
	{
		this.icon = icon;
		this.run = run;

		ToolTip = toolTip;
	}

	/// <summary>Whether what this turns on is on. Never, where nothing was said.</summary>
	public Func<bool> Active { get; init; }

	/// <summary>Whether it can be run. Always, where nothing was said.</summary>
	public Func<bool> Enabled { get; init; }

	public string ToolTip { get; }

	public string Icon => Read( icon, null );

	public bool IsActive => Read( Active, false );

	public bool IsEnabled => Read( Enabled, true );

	public void Execute() => run?.Invoke();

	//A setting that cannot be read is a button that says nothing, not a throw every frame.
	private static T Read<T>( Func<T> source, T fallback )
	{
		if ( source is null ) return fallback;

		try
		{
			return source();
		}
		catch ( Exception )
		{
			return fallback;
		}
	}
}
