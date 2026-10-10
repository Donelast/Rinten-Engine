using Rinten.MovieMaker;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

//Dropping a movie clip onto the timeline puts it in as a sequence block. The block is made as soon as the drag is over
//the timeline and moved with the pointer, so what will happen is visible before letting go; leaving takes it away again.
partial class Timeline : IEditorDropTarget
{
	private ProjectSequenceTrack? _draggedTrack;
	private ProjectSequenceBlock? _draggedBlock;

	public bool AcceptEditorDrops => true;

	private MovieResource? GetDraggedClip( DragData data )
	{
		if ( data.Assets.FirstOrDefault( x => x.AssetPath?.EndsWith( ".movie" ) ?? false ) is not { } assetData )
		{
			return null;
		}

		var assetTask = assetData.GetAssetAsync();

		if ( !assetTask.IsCompleted ) return null;
		if ( assetTask.Result?.LoadResource<MovieResource>() is not { } resource ) return null;
		if ( !Session.CanReferenceMovie( resource ) ) return null;

		return resource;
	}

	public DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		if ( _draggedBlock is null || _draggedTrack is null )
		{
			if ( GetDraggedClip( ev.Data ) is not { } resource )
			{
				return DropAction.Ignore;
			}

			var clip = resource.GetCompiled();

			_draggedTrack = Session.GetOrCreateTrack( resource );
			_draggedBlock = _draggedTrack.AddBlock( (0d, clip.Duration), default, resource );

			Session.TrackList.Update();
		}

		var scenePos = View.ToScene( ev.LocalPosition * ScaleFromScreen );
		var time = ScenePositionToTime( scenePos );

		_draggedBlock.TimeRange = (time, time + _draggedBlock.TimeRange.Duration);
		_draggedBlock.Transform = new MovieTransform( time );

		Session.TrackList.Find( _draggedTrack )?.MarkValueChanged();

		return DropAction.Link;
	}

	public void OnEditorDragLeave()
	{
		if ( _draggedBlock is not { } block || _draggedTrack is not { } track ) return;

		track.RemoveBlock( block );

		if ( track.IsEmpty )
		{
			track.Remove();
		}

		_draggedTrack = null;
		_draggedBlock = null;
	}

	public void OnEditorDragDrop( Widget.DragEvent ev )
	{
		if ( GetDraggedClip( ev.Data ) is null ) return;

		_draggedTrack = null;
		_draggedBlock = null;
	}
}
