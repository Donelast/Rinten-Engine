namespace Editor.MovieMaker;

#nullable enable

//Every key binding in the movie maker, gated on this panel having the keyboard.
partial class MovieEditor
{
	[Shortcut( "editor.save", "CTRL+S" )]
	public void OnSave() => Save();

	//No undo / redo of its own: the window's Ctrl+Z and Ctrl+Y go to whichever of this and the scene changed last - see IUndoSystemProvider.

	[Shortcut( "timeline.playtoggle", "Space" )]
	public void OnPlayToggle() => PlayToggle();

	[Shortcut( "timeline.recordtoggle", "ALT+F8" )]
	public void OnRecordToggle() => RecordToggle();

	[Shortcut( "timeline.navtostart", "Home" )]
	public void OnNavigateToStart() => NavigateToStart();

	[Shortcut( "timeline.navtoend", "End" )]
	public void OnNavigateToEnd() => NavigateToEnd();

	[Shortcut( "timeline.selectall", "CTRL+A" )]
	public void OnSelectAll() => SelectAll();

	[Shortcut( "timeline.cut", "CTRL+X" )]
	public void OnCut() => Cut();

	[Shortcut( "timeline.copy", "CTRL+C" )]
	public void OnCopy() => Copy();

	[Shortcut( "timeline.paste", "CTRL+V" )]
	public void OnPaste() => Paste();

	[Shortcut( "timeline.backspace", "BACKSPACE" )]
	public void OnBackspace() => Backspace();

	[Shortcut( "timeline.delete", "DEL" )]
	public void OnDelete() => Delete();

	[Shortcut( "timeline.insert", "TAB" )]
	public void OnInsert() => Insert();
}
