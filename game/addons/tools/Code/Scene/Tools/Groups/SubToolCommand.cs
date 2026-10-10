namespace Editor;

/// <summary>Button for a sub-tool, read from its type attributes; pressing the lit one keeps it lit</summary>
public sealed class SubToolCommand : IToolCommand
{
	private readonly EditorTool owner;
	private readonly EditorTool tool;
	private readonly TypeDescription type;

	public SubToolCommand( EditorTool owner, EditorTool tool )
	{
		this.owner = owner;
		this.tool = tool;

		type = EditorTypeLibrary.GetType( tool.GetType() );
	}

	/// <summary>The sub-tool this stands for.</summary>
	public EditorTool Tool => tool;

	public string Title => type?.Title ?? tool.GetType().Name;

	public string Description => type?.Description;

	public string Icon => type?.Icon;

	public string Shortcut => type?.GetAttribute<AliasAttribute>()?.Value?.FirstOrDefault();

	public string Badge => null;

	public string ToolTip => ToolCommandText.ToolTip( this );

	public bool IsActive => owner?.CurrentTool == tool;

	public bool IsEnabled => true;

	public void Execute()
	{
		if ( owner is null || owner.CurrentTool == tool ) return;

		owner.CurrentTool = tool;
	}

	/// <summary>A command for each of a tool's sub-tools, in the order the tool gave them, leaving out the ones already spoken for.</summary>
	public static IEnumerable<IToolCommand> ForSubToolsOf( EditorTool owner, Func<EditorTool, bool> represented = null )
	{
		if ( owner?.Tools is null ) yield break;

		foreach ( var subTool in owner.Tools )
		{
			if ( subTool is null ) continue;
			if ( represented is not null && represented( subTool ) ) continue;

			yield return new SubToolCommand( owner, subTool );
		}
	}
}
