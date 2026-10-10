using Rinten.UI;

namespace Editor;

/// <summary>A run of toolbar buttons drawn as one block.</summary>
public sealed class ToolbarStrip : Panel
{
	public ToolbarStrip()
	{
		AddClass( "toolbar-strip" );
	}

	/// <summary>Puts a command on the end of the strip.</summary>
	public EditorToolButton AddButton( IToolbarCommand command ) => AddChild( new EditorToolButton( command ) );
}
