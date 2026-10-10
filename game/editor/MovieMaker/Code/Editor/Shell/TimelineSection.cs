using Rinten.MovieMaker;
using Rinten.UI;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>The timeline, and the playback and snapping tools above it.</summary>
[StyleSheet( "MovieSection.cs.scss" )]
public sealed class TimelineSection : MovieSection
{
	public Timeline Timeline { get; }

	public TimelineSection( MovieEditor editor, Session session )
		: base( editor, session )
	{
		AddClass( "timeline-section" );

		Timeline = new Timeline( session ) { Parent = Body };

		BuildPlaybackGroup( session );
		BuildRateGroup( session );
		BuildEditModeGroup( session );
		BuildSnapGroup( session );
		BuildHistoryGroup( editor );
	}

	private void BuildPlaybackGroup( Session session )
	{
		var group = ToolBar.AddGroup( permanent: true );

		group.AddAction(
			new ToolBarItemDisplay( "Navigate to Start", "first_page",
				"Move the playhead to the start of the movie.",
				ShortcutIdentifier: "timeline.navtostart",
				Background: false ),
			Editor.NavigateToStart,
			() => session.PlayheadTime != 0d );

		group.AddToggle(
			new ToolBarItemDisplay( "Toggle Record", "radio_button_checked",
				"Start or stop recording live changes to tracks in the track list. Records the entire scene if the track list is empty.",
				ShortcutIdentifier: "timeline.recordtoggle",
				Background: false ),
			() => session.IsRecording,
			x => session.IsRecording = x )
			.ActiveTint = Theme.Red;

		group.AddToggle(
			new ToolBarItemDisplay( "Toggle Play", "play_arrow",
				"Start or stop playback.",
				ShortcutIdentifier: "timeline.playtoggle",
				Background: false ),
			() => session.IsPlaying,
			x => session.IsPlaying = x );

		group.AddAction(
			new ToolBarItemDisplay( "Navigate to End", "last_page",
				"Move the playhead to the end of the movie.",
				ShortcutIdentifier: "timeline.navtoend",
				Background: false ),
			Editor.NavigateToEnd,
			() => session.PlayheadTime != session.Duration );
	}

	private void BuildRateGroup( Session session )
	{
		var group = ToolBar.AddGroup( permanent: true );

		group.AddToggle(
			new ToolBarItemDisplay( "Toggle Looping", "repeat",
				"When enabled, playback will jump back to the start when reaching the end of the clip.",
				Background: false ),
			() => session.IsLooping,
			x => session.IsLooping = x );

		group.AddSlider( new MovieToolBarSlider( "Playback Rate",
			() => session.TimeScale,
			value => session.TimeScale = value )
		{
			Minimum = 0f,
			Maximum = 2f,
			Step = 0.1f,
			ResetValue = 1f
		} );

		group.AddReadout( () => $"x{session.TimeScale:F1}" );

		group.AddToggle(
			new ToolBarItemDisplay( "Toggle Playback Sync", "sync_lock",
				"When enabled, all MoviePlayers in the scene will sync with the currently open one when previewing a frame.",
				Background: false ),
			() => session.SyncPlayback,
			x => session.SyncPlayback = x );
	}

	private void BuildEditModeGroup( Session session )
	{
		if ( EditMode.AllTypes is not { Count: > 1 } editModes ) return;

		var group = ToolBar.AddGroup( permanent: true );

		foreach ( var editModeType in editModes )
		{
			var type = editModeType;

			group.AddToggle(
				new ToolBarItemDisplay( type.Title, type.Icon, type.Description ),
				() => type.IsMatchingType( session.EditMode ),
				state => session.SetEditMode( state ? type : null ) );
		}
	}

	private void BuildSnapGroup( Session session )
	{
		var group = ToolBar.AddGroup( permanent: true, alignRight: true );

		group.AddToggle(
			new ToolBarItemDisplay( "Object Snap", "align_horizontal_left",
				"Snap to objects in the timeline.",
				Background: false ),
			() => session.ObjectSnap,
			x => session.ObjectSnap = x );

		group.AddToggle(
			new ToolBarItemDisplay( "Frame Snap", "straighten",
				"Snap to the selected frame rate.",
				Background: false ),
			() => session.FrameSnap,
			x => session.FrameSnap = x );

		group.AddSpacing();

		group.AddPanel( new FrameRateDropDown( () => session.FrameRate, value => session.FrameRate = value ) );
	}

	private void BuildHistoryGroup( MovieEditor editor )
	{
		var group = ToolBar.AddGroup( permanent: true, alignRight: true );

		group.VisibleWhen( () => !editor.ShowHistory );

		group.AddToggle(
			new ToolBarItemDisplay( "Show History", "schedule",
				"Show the edit history panel.",
				Background: false ),
			() => false,
			_ => editor.ShowHistory = true );
	}
}

/// <summary>The frame rate the timeline snaps to, shown as the number itself.</summary>
public sealed class FrameRateDropDown : IconDropDown<int>
{
	private static readonly int[] Offered = [1, 2, 4, 5, 10, 15, 20, 24, 30, 48, 60, 90, 100, 120];

	public FrameRateDropDown( Func<int> getValue, Action<int> setValue )
		: base( "Snap Frame Rate", getValue, setValue )
	{
		AddClass( "frame-rate-dropdown" );
	}

	protected override IEnumerable<int> OnGetOptions() => MovieTime.SupportedFrameRates.Intersect( Offered );

	protected override string OnGetOptionTitle( int option ) => $"{option} FPS";

	protected override void OnBuildPreview( Rinten.UI.Panel host, int value )
	{
		host.AddChild<Rinten.UI.Label>( "text" ).Text = $"{value} FPS";
	}
}
