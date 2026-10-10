using Editor.NodeEditor;
using Rinten.MovieMaker;
using Rinten.MovieMaker.Properties;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using DisplayInfo = Rinten.MovieMaker.Properties.DisplayInfo;

namespace Editor.MovieMaker;

#nullable enable

//What right clicking a track offers: locking, sub-tracks, presets, removal.
partial class TrackRowPanel
{
	public void ShowContextMenu()
	{
		var menu = new Menu();
		var trackViews = View.TrackList.SelectedTracks.ToArray();

		if ( trackViews.Length == 1 )
		{
			menu.AddHeading( $"{View.Title} Track" );

			if ( View.Track is ProjectSequenceTrack sequenceTrack )
			{
				var rename = menu.AddMenu( "Rename", "edit" );

				rename.AddLineEdit( "Name", sequenceTrack.Name, autoFocus: true, onSubmit: OnRename );
			}

			AddCommonContextMenuOptions( menu, trackViews );

			if ( CanMoveToRoot )
			{
				menu.AddOption( "Move to Root", "subdirectory_arrow_left", MoveToRoot );
			}

			if ( CanMoveToParent( out var parentTrack ) )
			{
				menu.AddOption( "Move to Parent", "subdirectory_arrow_right", () => MoveToParent( parentTrack ) );
			}

			if ( CanHaveSubTracks )
			{
				CreateSubTrackMenu( menu );
			}
		}
		else
		{
			menu.AddHeading( "Selected Tracks" );

			AddCommonContextMenuOptions( menu, trackViews );
		}

		menu.OpenAtCursor( this );
	}

	private void AddCommonContextMenuOptions( Menu menu, IReadOnlyList<TrackView> trackViews )
	{
		var anyLocked = trackViews.Any( x => x.IsLockedSelf );
		var anyUnlocked = trackViews.Any( x => !x.IsLockedSelf );

		if ( anyUnlocked )
		{
			menu.AddOption( "Lock", "lock", () =>
			{
				foreach ( var track in trackViews )
				{
					track.IsLockedSelf = true;
				}
			} );
		}

		if ( anyLocked )
		{
			menu.AddOption( "Unlock", "lock_open", () =>
			{
				foreach ( var track in trackViews )
				{
					track.IsLockedSelf = false;
				}
			} );
		}

		var anyExpanded = trackViews.Any( x => x is { IsExpanded: true, Children.Count: > 0 } );
		var anyCollapsed = trackViews.Any( x => x is { IsExpanded: false, Children.Count: > 0 } );

		if ( anyCollapsed )
		{
			menu.AddOption( "Expand", "add", () =>
			{
				foreach ( var track in trackViews )
				{
					if ( track is { IsExpanded: false, Children.Count: > 0 } )
					{
						track.IsExpanded = true;
					}
				}

				View.TrackList.Update();
			} );
		}

		if ( anyExpanded )
		{
			menu.AddOption( "Collapse", "remove", () =>
			{
				foreach ( var track in trackViews )
				{
					if ( track is { IsExpanded: true, Children.Count: > 0 } )
					{
						track.IsExpanded = false;
					}
				}

				View.TrackList.Update();
			} );
		}

		menu.AddOption( "Remove", "delete", () =>
		{
			foreach ( var track in trackViews )
			{
				track.Remove();
			}
		} );

		menu.AddOption( "Save As Sequence..", "theaters", () =>
		{
			var timeRange = new MovieTimeRange( 0, Session.Duration );

			Session.Editor.SaveAsDialog( "Save As Sequence..",
				() => Session.CreateSequence( [.. trackViews.SelectDescendants()], timeRange ),
				result =>
				{
					using var historyScope = Session.History.Push( "Create Sequence" );

					foreach ( var trackView in trackViews )
					{
						trackView.Track.Remove();
					}

					Session.GetOrCreateTrack( result.Resource ).AddBlock( result.StartTime, result.Resource );
					Session.TrackList.Update();
				} );
		} );
	}

	private static bool? GetAggregateLockState( IEnumerable<TrackView> trackViews )
	{
		var anyLocked = false;
		var anyUnlocked = false;

		foreach ( var view in trackViews )
		{
			if ( view.IsLockedSelf ) anyLocked = true;
			else anyUnlocked = true;
		}

		return anyLocked == anyUnlocked ? null : anyLocked;
	}

	//True if this is a child GameObject track, and not representing a bone object.
	private bool CanMoveToRoot
	{
		get
		{
			if ( View is not { Target: ITrackReference<GameObject> reference, Parent: not null } )
			{
				return false;
			}

			//Don't allow moving bone tracks to root

			return !reference.IsBound || (reference.Value?.Flags & GameObjectFlags.Bone) == 0;
		}
	}

	//Should we show sub-track menu options?
	private bool CanHaveSubTracks => View.Track is not ProjectSequenceTrack && (View.Children.Count > 0 || TrackProperty.GetAll( View.Target ).Any());

	//True if this is a GameObject track, and the object's parent also has a track that this track isn't parented to.
	private bool CanMoveToParent( [NotNullWhen( true )] out IProjectReferenceTrack? parentTrack )
	{
		parentTrack = null;

		if ( View is not { Target: ITrackReference<GameObject> reference } )
		{
			return false;
		}

		if ( !reference.IsBound || reference.Value is not { Parent: { } parent } )
		{
			return false;
		}

		parentTrack = Session.GetTrack( parent ) as IProjectReferenceTrack;

		if ( parentTrack is null )
		{
			return false;
		}

		//Already parented

		return View.Parent?.Track != parentTrack;
	}

	private record AvailableTrackProperty( string Name, DisplayInfo Display, Type Type, Func<IProjectTrack> Create );

	private void CreateSubTrackMenu( Menu parent )
	{
		parent.AddHeading( "Sub-Tracks" );

		var menu = parent.AddMenu( "Add / Remove", "playlist_add_check" );

		var session = Session;
		var availableTracks = new List<AvailableTrackProperty>();

		if ( View.Target is ITrackReference<GameObject> { IsBound: true, Value: { Components.Count: > 0 } go } )
		{
			foreach ( var component in go.Components.GetAll() )
			{
				var type = component.GetType();
				var typeDef = TypeLibrary.GetType( type );

				availableTracks.Add( new AvailableTrackProperty( type.Name, new DisplayInfo( typeDef.Title, "Components", typeDef.Description, typeDef.Icon ), type,
					() => session.GetOrCreateTrack( component ) ) );
			}
		}

		foreach ( var property in TrackProperty.GetAll( View.Target ) )
		{
			availableTracks.Add( new AvailableTrackProperty( property.Name, property.Display, property.Type,
				() => session.GetOrCreateTrack( View.Track, property.Name ) ) );
		}

		Action? updateActive = null;
		LineEdit? filterLineEdit = null;

		void UpdateOptions( string? filter )
		{
			updateActive = null;

			menu.RemoveMenus();
			menu.RemoveOptions();

			foreach ( var widget in menu.Widgets )
			{
				if ( widget.IsAncestorOf( filterLineEdit! ) ) continue;

				menu.RemoveWidget( widget );
			}

			var filtered = string.IsNullOrEmpty( filter )
				? availableTracks
				: availableTracks.Where( x => x.Name.Contains( filter, StringComparison.OrdinalIgnoreCase ) );

			menu.AddOptions( filtered,
				getPath: x => Menu.GetSplitPath( $"{x.Display.Category}/{x.Display.Title}" ),
				flat: !string.IsNullOrEmpty( filter ),
				createOption: ( m, display, value ) =>
				{
					var option = new TrackToggleOptionPanel( display.Name, false, create =>
					{
						using var scope = session.History.Push( $"{(create ? "Create" : "Remove")} Track ({value.Name})" );

						IProjectTrack? track = null;

						if ( create )
						{
							track = value.Create();
						}
						else
						{
							View.Children
								.FirstOrDefault( x => x.Track.Name == value.Name )?
								.Remove();
						}

						session.TrackList.Update();
						session.ClipModified();

						if ( track is null || session.TrackList.Find( track ) is not { } trackView ) return;

						trackView.ExpandAncestors();
						trackView.IsSelected = true;

						TrackList.Timeline.ScrollToTrack( trackView );
					} );

					updateActive += () => option.IsActive = View.Children.Any( x => x.Track.Name == value.Name );

					m.AddWidget( new MenuPanelItem( option ) );
				} );

			updateActive?.Invoke();
		}

		filterLineEdit = menu.AddLineEdit( "Filter", autoFocus: true, onChange: UpdateOptions );

		UpdateOptions( null );

		menu.AboutToShow += () => updateActive?.Invoke();

		if ( availableTracks.Any( x => session.GetTrack( View.Track, x.Name ) is null ) )
		{
			parent.AddOption( "Add All", "playlist_add", () =>
			{
				foreach ( var available in availableTracks )
				{
					session.GetOrCreateTrack( View.Track, available.Name );
				}

				session.TrackList.Update();
				session.ClipModified();
			} );
		}

		if ( View.Children.Count > 0 )
		{
			parent.AddOption( "Remove All", "playlist_remove", () =>
			{
				foreach ( var child in View.Children.ToArray() )
				{
					child.Track.Remove();
				}

				session.TrackList.Update();
				session.ClipModified();
			} );

			parent.AddOption( "Remove Empty", "cleaning_services", RemoveEmptyChildren );
		}

		CreatePresetMenu( parent );

		if ( View.Children.Count <= 0 ) return;

		parent.AddSeparator();

		var lockState = GetAggregateLockState( View.Children );

		if ( lockState != true )
		{
			parent.AddOption( "Lock All", "lock", LockChildren );
		}

		if ( lockState != false )
		{
			parent.AddOption( "Unlock All", "lock_open", UnlockChildren );
		}
	}

	private void CreatePresetMenu( Menu parent )
	{
		var session = Session;

		var matching = TrackPreset.BuiltInPresets
			.Concat( session.Config.TrackPresets )
			.Where( x => x.Root.Matches( View.Target ) )
			.ToArray();

		var canLoad = matching.Length > 0;
		var canSave = View.Children.Any();

		if ( !canLoad && !canSave ) return;

		if ( canLoad )
		{
			var menu = parent.AddMenu( "Load Preset", "menu_open" );

			TrackPresetMenu.Populate( menu, matching, [View] );
		}

		if ( canSave )
		{
			var menu = parent.AddMenu( "Save Preset", "save" );

			menu.AddLineEdit( "Title", autoFocus: true, onSubmit: title =>
			{
				if ( string.IsNullOrWhiteSpace( title ) ) return;

				var preset = new TrackPreset( new TrackPresetMetadata( title ), TrackPresetNode.FromTrackView( View ) );

				session.Config.TrackPresets.Add( preset );
				session.SaveConfig();
			} );
		}
	}

	private void OnRename( string name )
	{
		if ( View.Track is not ProjectSequenceTrack sequenceTrack ) return;

		sequenceTrack.Name = name;

		if ( _label is { } label ) label.Text = name;

		//Track order might have changed

		Session.TrackList.Update();
	}

	private void MoveToRoot()
	{
		if ( View.Track is not IProjectTrackInternal track ) return;
		if ( View.Parent?.Track is not IProjectTrackInternal parentTrack ) return;

		parentTrack.RemoveChild( track );

		Session.TrackList.Update();
	}

	private void MoveToParent( IProjectReferenceTrack parentTrack )
	{
		if ( View.Track is not IProjectTrackInternal track ) return;
		if ( parentTrack is not IProjectTrackInternal newParentTrack ) return;

		if ( View.Parent?.Track is IProjectTrackInternal currentParentTrack )
		{
			if ( currentParentTrack == parentTrack ) return;

			currentParentTrack.RemoveChild( track );
		}

		newParentTrack.AddChild( track );

		Session.TrackList.Update();
	}

	private void RemoveEmptyChildren()
	{
		foreach ( var child in View.Children.ToArray() )
		{
			RemoveEmptyCore( child );
		}

		Session.TrackList.Update();
	}

	private static bool RemoveEmptyCore( TrackView view )
	{
		var allChildrenRemoved = true;

		foreach ( var child in view.Children.ToArray() )
		{
			allChildrenRemoved &= RemoveEmptyCore( child );
		}

		if ( allChildrenRemoved && view.IsEmpty )
		{
			view.Remove();
			return true;
		}

		return false;
	}

	private void LockChildren()
	{
		foreach ( var child in View.Children )
		{
			child.IsLockedSelf = true;
		}
	}

	private void UnlockChildren()
	{
		foreach ( var child in View.Children )
		{
			child.IsLockedSelf = false;
		}
	}
}

/// <summary>The list of track presets that can be loaded onto a set of tracks.</summary>
public static class TrackPresetMenu
{
	public static void Populate( Menu menu, IReadOnlyList<TrackPreset> presets, IReadOnlyList<TrackView> rootViews )
	{
		if ( rootViews.Count == 0 ) return;

		var session = rootViews[0].TrackList.Session;

		Action? updateActive = null;

		foreach ( var preset in presets )
		{
			var option = new TrackToggleOptionPanel( preset.Meta.Title, false, create =>
			{
				using var scope = session.History.Push( $"{(create ? "Create" : "Remove")} Preset Tracks ({preset.Meta.Title})" );

				IProjectTrack[] createdTracks = [];

				foreach ( var rootView in rootViews )
				{
					if ( create )
					{
						createdTracks = [.. session.LoadPreset( rootView.Track, rootView.Target, preset.Root )];
					}
					else
					{
						session.RemovePreset( rootView.Track, rootView.Target, preset.Root );
					}
				}

				session.TrackList.Update();
				session.ClipModified();

				session.TrackList.ExpandAncestors( createdTracks );
				session.TrackList.SelectAll( createdTracks );

				updateActive?.Invoke();
			}, TrackPreset.BuiltInPresets.Contains( preset ) ? null : () =>
			{
				session.Config.TrackPresets.Remove( preset );
				session.SaveConfig();
			} );

			option.Tooltip = preset.Meta.Description;

			updateActive += () =>
			{
				var totalCount = rootViews.Max( x => preset.AvailableTrackCount( x.Track, session.Binder ) );
				var matchingCount = rootViews.Min( x => preset.MatchingTrackCount( x.Track ) );

				option.Title = $"{preset.Meta.Title} ({matchingCount} / {totalCount} Tracks)";
				option.IsActive = rootViews.All( x => preset.AllTracksExist( x.Track, session.Binder ) );
			};

			menu.AddWidget( new MenuPanelItem( option ) );
		}

		menu.AboutToShow += () => updateActive?.Invoke();
	}
}
