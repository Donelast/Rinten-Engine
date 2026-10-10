using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

//It says the drag will land, and it takes the drop itself so that letting go anywhere near the top of the list works rather than only exactly on a row.
/// <summary>The dashed box that appears over the track list while something is being dragged.</summary>
public sealed class TrackDropTargetPanel : Panel, IEditorDropTarget
{
	private readonly TrackListPanel _trackList;

	public TrackDropTargetPanel( TrackListPanel trackList )
	{
		_trackList = trackList;

		AddClass( "track-drop-target" );
		AddClass( "hidden" );

		var inner = AddChild<Panel>( "inner" );

		inner.AddChild<IconPanel>( "icon" ).Text = "playlist_add";
		inner.AddChild<Rinten.UI.Label>( "text" ).Text = "Drag here to create a new track.";
	}

	public bool AcceptEditorDrops => true;

	public DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accepted = TrackListPanel.HasDraggedTracks( ev.Data );

		SetClass( "hovered", accepted );

		return accepted ? DropAction.Link : DropAction.Ignore;
	}

	public void OnEditorDragLeave() => SetClass( "hovered", false );

	public void OnEditorDragDrop( Widget.DragEvent ev ) => _trackList.AddTracksFromDrag( ev.Data );
}
