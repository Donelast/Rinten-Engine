using Rinten.MovieMaker;
using static Editor.MovieMaker.Session;

namespace Editor.MovieMaker;

#nullable enable

//Opening, saving and swapping the clip the editor is pointed at.
partial class MovieEditor
{
	public void SwitchToEmbedded()
	{
		if ( Session!.Resource is EmbeddedMovieResource ) return;

		Session.Player.Resource = new EmbeddedMovieResource
		{
			Compiled = Session.Resource.Compiled,
			EditorData = Session.Project.Serialize()
		};

		Switch( Session.Player );
	}

	public void SwitchToNewEmbedded()
	{
		if ( Session is not { } session ) return;

		if ( session is { Resource: EmbeddedMovieResource, Project.IsEmpty: false } )
		{
			Dialog.AskConfirm( ConfirmSwitchToNewEmbedded, question: "The current embedded clip will be lost. Are you sure?" );
			return;
		}

		if ( session is { Resource: MovieResource resource, HasUnsavedChanges: true } )
		{
			Dialog.AskConfirm( () =>
			{
				session.Save();
				ConfirmSwitchToNewEmbedded();
			}, ConfirmSwitchToNewEmbedded, question: $"Save unsaved changes to {resource.ResourceName}.movie?", okay: "Save", cancel: "Don't Save" );
			return;
		}

		ConfirmSwitchToNewEmbedded();
	}

	private void ConfirmSwitchToNewEmbedded()
	{
		if ( Session is not { } session ) return;

		//The old embedded session is being thrown away, so drop it from the list
		_sessions.Remove( new SessionKey( session.Player.Id, null ) );

		var player = session.Player;

		player.Resource = new EmbeddedMovieResource();

		Switch( player );
	}

	//A movie asked for before there was a session to play it in.
	private MovieResource? _pending;

	/// <summary>Opens a movie asked for from outside, once there is a session to put it in.</summary>
	public void OpenResource( MovieResource resource )
	{
		_pending = resource;

		OpenPending();
	}

	//Hands over the movie that was waiting for a session.
	internal void OpenPending()
	{
		if ( _pending is null ) return;
		if ( Session is null ) return;

		var resource = _pending;

		_pending = null;

		SwitchResource( resource );
	}

	public void SwitchResource( MovieResource resource )
	{
		if ( Session is not { } session ) return;
		if ( session.Root.Resource == resource ) return;

		if ( session is { Resource: EmbeddedMovieResource, Project.IsEmpty: false } )
		{
			Dialog.AskConfirm( () =>
			{
				ConfirmedSwitchResource( resource );
			}, question: "Switching to a clip resource will cause your embedded clip to be lost. Are you sure?" );
			return;
		}

		if ( session is { Resource: MovieResource unsaved, HasUnsavedChanges: true } )
		{
			Dialog.AskConfirm( () =>
				{
					session.Save();
					ConfirmedSwitchResource( resource );
				}, () => ConfirmedSwitchResource( resource ), question: $"Save unsaved changes to {unsaved.ResourceName}.movie?",
				okay: "Save", cancel: "Don't Save" );
			return;
		}

		ConfirmedSwitchResource( resource );
	}

	private void ConfirmedSwitchResource( MovieResource resource )
	{
		Session!.Player.Resource = resource;

		Switch( Session.Player );
	}

	public void Save() => Session?.Save();

	public void SaveFileAs() => SaveAsDialog( "Save Movie As..",
		() => new CreateSequenceResult( new MovieResource { Compiled = Session!.Project.Compile(), EditorData = Session.Project.Serialize() } ),
		result => ConfirmedSwitchResource( result.Resource ) );

	public void SaveAsDialog( string title, Func<CreateSequenceResult> createResource, Action<CreateSequenceResult>? afterSave = null )
	{
		var fd = new FileDialog( null );

		fd.Title = title;
		fd.Directory = Project.Current.GetAssetsPath();
		fd.DefaultSuffix = ".movie";
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Movie Clip File (*.movie)" );

		if ( !fd.Execute() )
			return;

		var sceneAsset = AssetSystem.CreateResource( "movie", fd.SelectedFile );
		var result = createResource();

		sceneAsset.SaveToDisk( result.Resource );

		afterSave?.Invoke( result );
	}
}
