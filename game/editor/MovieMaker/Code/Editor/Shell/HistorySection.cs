using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>Every change made this session, and the undo and redo that walk it.</summary>
[StyleSheet( "MovieSection.cs.scss" )]
public sealed class HistorySection : MovieSection
{
	public HistoryListPanel List { get; }

	public HistorySection( MovieEditor editor, Session session )
		: base( editor, session )
	{
		AddClass( "history-section" );

		List = new HistoryListPanel( session ) { Parent = Body };

		BuildUndoGroup( session );
		BuildTitleGroup();
		BuildHideGroup( editor );
	}

	private void BuildUndoGroup( Session session )
	{
		var group = ToolBar.AddGroup( permanent: true );

		group.AddAction(
			new ToolBarItemDisplay( "Undo", "undo",
				"Revert the last change made to the movie clip.",
				ShortcutIdentifier: "editor.undo" ),
			session.Undo,
			() => session.History.CanUndo );

		group.AddAction(
			new ToolBarItemDisplay( "Redo", "redo",
				"Reapply the last undone change made to the movie clip.",
				ShortcutIdentifier: "editor.redo" ),
			session.Redo,
			() => session.History.CanRedo );
	}

	private void BuildTitleGroup()
	{
		var group = ToolBar.AddGroup( permanent: true );

		group.AddLabel( "History" ).AddClass( "title" );
	}

	private void BuildHideGroup( MovieEditor editor )
	{
		var group = ToolBar.AddGroup( permanent: true );

		group.AddToggle(
			new ToolBarItemDisplay( "Hide History", "schedule",
				"Hide the edit history panel.",
				Background: false ),
			() => true,
			_ => editor.ShowHistory = false );
	}
}
