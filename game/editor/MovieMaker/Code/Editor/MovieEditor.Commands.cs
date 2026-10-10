namespace Editor.MovieMaker;

#nullable enable

//What the editing keys do, and which panel they do it to. The shortcuts themselves are on the window, because that is what the
//shortcut system can gate on. Which of the two lists a key means is decided here, from whichever section was last clicked in.
partial class MovieEditor
{
	/// <summary>The section the pointer was last used in, which the editing keys go to.</summary>
	public MovieSection? ActiveSection { get; internal set; }

	public void SelectAll()
	{
		if ( ActiveSection is TrackListSection && Session is { } session )
		{
			foreach ( var track in session.TrackList.VisibleTracks )
			{
				track.IsSelected = true;
			}

			return;
		}

		Session?.EditMode?.SelectAll();
	}

	public void Cut() => Session?.EditMode?.Cut();

	public void Copy() => Session?.EditMode?.Copy();

	public void Paste() => Session?.EditMode?.Paste();

	public void Backspace() => Session?.EditMode?.Backspace();

	public void Delete() => Session?.EditMode?.Delete();

	public void Insert() => Session?.EditMode?.Insert();
}
