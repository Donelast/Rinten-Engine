using Rinten.UI;

namespace Editor.MdlEditor.Nodes;

//The badge on a selector node saying it decides which of its inputs runs.
internal static class EntrySelectionHint
{
	public static void Add( Panel body )
	{
		var badge = body.AddChild<IconPanel>( "entry-hint" );

		badge.Text = "login";
		badge.Tooltip = "Chooses which entry to play";
	}
}
