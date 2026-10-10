namespace Editor.MeshEditor;

//Row of move-mode buttons for settings panels; the keys live on ToolSidebarPanel
class MoveModePanel : EditorPanel
{
	readonly MeshTool _tool;

	public MoveModePanel( MeshTool tool )
	{
		_tool = tool;

		AddClass( "row" );
		AddClass( "gap-2" );

		foreach ( var type in EditorTypeLibrary.GetTypes<MoveMode>().OrderBy( x => x.Order ) )
		{
			if ( type.IsAbstract ) continue;

			var button = AddChild( new EditorToolButton( new ToolbarCommand( type.Icon, ToolTipFor( type ), () => Activate( type ) )
			{
				Active = () => type.TargetType == _tool.MoveMode?.GetType()
			} ) );

			button.AddClass( "chip" );
		}
	}

	//What it is called, what it is for, and the key that picks it.
	static string ToolTipFor( TypeDescription type )
	{
		var title = type.Title;

		if ( type.GetAttribute<AliasAttribute>() is AliasAttribute alias && !string.IsNullOrEmpty( alias.Value.FirstOrDefault() ) )
		{
			//A mode is aliased as what it is - "mesh.position.mode" - and the key is held against what presses it -
			//"tools.position-tool". Asking here with the mode's own alias found nothing, so no button said its key.
			var keys = MeshMoveModes.KeysFor( alias.Value.FirstOrDefault() );

			if ( !string.IsNullOrEmpty( keys ) )
			{
				title += $" ({keys.Trim().ToUpperInvariant()})";
			}
		}

		return $"<b>{title}</b><br>{type.Description}";
	}

	void Activate( TypeDescription type )
	{
		if ( type.TargetType == _tool.MoveMode?.GetType() ) return;

		_tool.MoveMode = type.Create<MoveMode>();
	}
}
