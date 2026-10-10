using Editor.Inspectors;
using Rinten.UI;

namespace Editor.MdlEditor;

internal enum RecorderPlaybackSpeed
{
	[Title( "0.25x" )]
	Quarter,
	[Title( "0.5x" )]
	Half,
	[Title( "1x" )]
	Normal,
	[Title( "2x" )]
	Double,
	[Title( "4x" )]
	Quadruple
}

//Records what the live graph did and plays it back a frame at a time. A strip of buttons and a scrubber in a rounded well - so it
//is markup and RecorderToolbar.cs.scss , with the buttons coming from PanelToolbar rather than being seven hand-built icon squares.
[StyleSheet( "RecorderToolbar.cs.scss" )]
internal sealed class RecorderToolbar : EditorPanel
{
	private readonly IPreviewHost _host;
	private readonly PanelToolbarButton _recordButton;
	private readonly PanelToolbarButton _clearButton;
	private readonly PanelToolbarButton _stepBackwardButton;
	private readonly PanelToolbarButton _playButton;
	private readonly PanelToolbarButton _stopButton;
	private readonly PanelToolbarButton _stepForwardButton;
	private readonly PanelToolbarButton _loopButton;
	private readonly DropDown _speed;
	private readonly SliderControl _timeline;
	private readonly List<AnimGraphRecordedUpdateInfo> _frames = [];
	private readonly List<float> _frameTimes = [];

	private RecorderPlaybackSpeed _playbackSpeed = RecorderPlaybackSpeed.Normal;

	private bool _wasPaused;
	private bool _reviewActive;
	private bool _playing;
	private bool _loop;
	private bool _updatingTimeline;
	private float _playbackAccumulator;

	private SceneModel Model => _host.PreviewModel;
	private float PlaybackSpeed => _playbackSpeed switch
	{
		RecorderPlaybackSpeed.Quarter => 0.25f,
		RecorderPlaybackSpeed.Half => 0.5f,
		RecorderPlaybackSpeed.Double => 2f,
		RecorderPlaybackSpeed.Quadruple => 4f,
		_ => 1f
	};

	public RecorderToolbar( IPreviewHost host )
	{
		_host = host;

		AddClass( "recorder-toolbar" );

		//It hears the keys the transport is driven by, so it has to be able to hold the keyboard.
		AcceptsFocus = true;

		var controls = AddChild<PanelToolbar>();

		_recordButton = controls.AddIcon( "fiber_manual_record", "Record live graph", ToggleRecording );
		_recordButton.AddClass( "record" );

		_clearButton = controls.AddIcon( "delete", "Clear recording", Clear );

		controls.AddSeparator();

		_stepBackwardButton = controls.AddIcon( "skip_previous", "Previous update (Left)", StepBackward );
		_playButton = controls.AddIcon( "play_arrow", "Play recording (Space)", TogglePlayback );
		_stopButton = controls.AddIcon( "stop", "Stop review", StopReview );
		_stepForwardButton = controls.AddIcon( "skip_next", "Next update (Right)", StepForward );
		_loopButton = controls.AddToggle( "repeat", "Loop playback", false, value => _loop = value );

		_speed = AddChild<ControlDropDown>( "speed" );
		_speed.Tooltip = "Playback speed";

		//Handed the enum rather than a list: a dropdown fills itself from the type, titles and all, so the speeds are named once - on the enum.
		_speed.Value = _playbackSpeed;
		_speed.ValueChanged = OnSpeedPicked;

		//The scrubber stands in a well of its own, the way a timeline reads everywhere else in the editor.
		var track = AddChild<Panel>( "track" );

		_timeline = track.AddChild( new SliderControl( 0f, 0f, 1f ) { ShowBackground = false } );
		_timeline.AddClass( "toolbar-slider" );
		_timeline.OnValueChanged = Scrub;

		Refresh();
	}

	private void OnSpeedPicked( string value )
	{
		if ( Enum.TryParse<RecorderPlaybackSpeed>( value, out var speed ) )
			_playbackSpeed = speed;
	}

	private void ToggleRecording()
	{
		var model = Model;
		if ( !model.IsValid() || model.IsAnimGraph2RecordingPlaybackActive )
			return;

		if ( model.IsAnimGraph2Recording )
		{
			model.StopAnimGraph2Recording();
		}
		else
		{
			_frames.Clear();
			_frameTimes.Clear();
			_playing = false;
			model.StartAnimGraph2Recording();
		}

		Refresh();
	}

	private bool StartReview()
	{
		var model = Model;
		if ( !model.IsValid() || model.IsAnimGraph2Recording || !model.HasAnimGraph2Recording )
			return false;

		if ( model.IsAnimGraph2RecordingPlaybackActive )
			return true;

		_wasPaused = _host.IsPreviewPaused;
		if ( !model.StartAnimGraph2RecordingPlayback() )
			return false;

		_host.SetRecordingReviewMode( true, true );
		_reviewActive = true;
		_playbackAccumulator = 0f;
		if ( ApplyRecordedUpdate( 0 ) )
			return true;

		StopReview();
		return false;
	}

	private void StopReview()
	{
		var model = Model;
		if ( !_reviewActive && (!model.IsValid() || !model.IsAnimGraph2RecordingPlaybackActive) )
			return;

		_playing = false;
		_playbackAccumulator = 0f;
		if ( model.IsValid() && model.IsAnimGraph2RecordingPlaybackActive )
			model.StopAnimGraph2RecordingPlayback();

		if ( _reviewActive )
		{
			_host.SetRecordingReviewMode( false, _wasPaused );
			_reviewActive = false;
		}

		Refresh();
	}

	private void TogglePlayback()
	{
		var model = Model;
		if ( !model.IsValid() || model.IsAnimGraph2Recording )
			return;

		if ( !model.IsAnimGraph2RecordingPlaybackActive && !StartReview() )
			return;

		if ( model.AnimGraph2CurrentRecordedUpdate >= model.AnimGraph2RecordedUpdateCount - 1 )
			ApplyRecordedUpdate( 0 );

		_playing = !_playing;
		_playbackAccumulator = 0f;
		Refresh();
	}

	private void Seek( int updateIndex )
	{
		if ( !StartReview() )
			return;

		_playing = false;
		_playbackAccumulator = 0f;
		ApplyRecordedUpdate( updateIndex );
	}

	private void Scrub( float value )
	{
		if ( !_updatingTimeline )
			Seek( (int)MathF.Round( value ) );
	}

	private void JumpToStart() => Seek( 0 );

	private void JumpToEnd()
	{
		var count = Model.IsValid() ? Model.AnimGraph2RecordedUpdateCount : 0;
		if ( count > 0 )
			Seek( count - 1 );
	}

	private void StepBackward()
	{
		var current = Model.IsValid() ? Model.AnimGraph2CurrentRecordedUpdate : 0;
		Seek( Math.Max( 0, current - 1 ) );
	}

	private void StepForward()
	{
		var model = Model;
		var count = model.IsValid() ? model.AnimGraph2RecordedUpdateCount : 0;
		if ( count > 0 )
			Seek( Math.Min( count - 1, model.AnimGraph2CurrentRecordedUpdate + 1 ) );
	}

	private bool ApplyRecordedUpdate( int updateIndex, bool refresh = true )
	{
		if ( !_host.ApplyRecordedUpdate( updateIndex ) )
			return false;

		if ( refresh )
			Refresh();

		return true;
	}

	internal void Clear()
	{
		var model = Model;
		if ( !model.IsValid() )
		{
			if ( _reviewActive )
				StopReview();

			return;
		}

		if ( model.IsAnimGraph2Recording )
			model.StopAnimGraph2Recording();

		if ( model.IsAnimGraph2RecordingPlaybackActive )
			StopReview();

		model.ClearAnimGraph2Recording();
		_frames.Clear();
		_frameTimes.Clear();
		_playing = false;
		_playbackAccumulator = 0f;
		Refresh();
	}

	private void RefreshRecordedFrames( SceneModel model, int count )
	{
		if ( count < _frames.Count )
		{
			_frames.Clear();
			_frameTimes.Clear();
		}

		var elapsed = _frameTimes.Count > 0 ? _frameTimes[^1] : 0f;
		for ( var i = _frames.Count; i < count; i++ )
		{
			if ( !model.TryGetAnimGraph2RecordedUpdateInfo( i, out var info ) )
				break;

			_frames.Add( info );
			elapsed += info.DeltaTime;
			_frameTimes.Add( elapsed );
		}
	}

	private void AdvancePlayback()
	{
		var model = Model;
		if ( !_playing || !model.IsValid() || !model.IsAnimGraph2RecordingPlaybackActive )
			return;

		var count = model.AnimGraph2RecordedUpdateCount;
		if ( count <= 1 )
		{
			_playing = false;
			return;
		}

		_playbackAccumulator += RealTime.Delta * PlaybackSpeed;
		var current = model.AnimGraph2CurrentRecordedUpdate;

		while ( current < count - 1 )
		{
			var next = current + 1;
			var frameDuration = next < _frames.Count ? _frames[next].DeltaTime : 0f;
			if ( frameDuration > 0f && _playbackAccumulator < frameDuration )
				break;

			_playbackAccumulator = Math.Max( 0f, _playbackAccumulator - frameDuration );
			if ( !ApplyRecordedUpdate( next, refresh: false ) )
			{
				_playing = false;
				return;
			}

			current = next;
		}

		if ( current < count - 1 )
			return;

		if ( _loop )
		{
			_playbackAccumulator = 0f;
			ApplyRecordedUpdate( 0, refresh: false );
			return;
		}

		_playing = false;
		_playbackAccumulator = 0f;
	}

	private void Refresh()
	{
		var model = Model;
		var valid = model.IsValid();
		var recording = valid && model.IsAnimGraph2Recording;
		var reviewing = valid && model.IsAnimGraph2RecordingPlaybackActive;
		var hasRecording = valid && model.HasAnimGraph2Recording;
		var canReview = hasRecording && !recording;
		var count = valid ? model.AnimGraph2RecordedUpdateCount : 0;
		var current = reviewing ? model.AnimGraph2CurrentRecordedUpdate : 0;

		if ( valid )
			RefreshRecordedFrames( model, count );

		_recordButton.Enabled = valid && !reviewing;
		_recordButton.IsActive = recording;
		_recordButton.SetIcon( recording ? "stop" : "fiber_manual_record" );
		_recordButton.Tooltip = recording ? "Stop recording" : "Record live graph";
		_clearButton.Enabled = canReview && !reviewing;

		_stepBackwardButton.Enabled = canReview && (!reviewing || current > 0);
		_playButton.Enabled = canReview;
		_playButton.IsActive = _playing;
		_playButton.SetIcon( _playing ? "pause" : "play_arrow" );
		_playButton.Tooltip = _playing ? "Pause recording (Space)" : "Play recording (Space)";
		_stopButton.Enabled = reviewing;
		_stepForwardButton.Enabled = canReview && (!reviewing || current < count - 1);
		_loopButton.Enabled = canReview;
		_speed.Disabled = !canReview;

		_updatingTimeline = true;
		_timeline.ReadOnly = !canReview;
		_timeline.Max = Math.Max( 0, count - 1 );

		//Not while it is being dragged: the playhead and the pointer would each write the value every frame, and the thumb would not follow the hand.
		if ( !_timeline.IsDragging )
			_timeline.Value = current;

		_updatingTimeline = false;

		var elapsed = current >= 0 && current < _frameTimes.Count ? _frameTimes[current] : 0f;
		var duration = _frameTimes.Count > 0 ? _frameTimes[^1] : 0f;

		string infoText;
		if ( recording )
			infoText = $"Recording · {count} updates";
		else if ( reviewing && current >= 0 && current < _frames.Count )
			infoText = RecordedUpdateText( current, count, _frames[current] );
		else if ( hasRecording )
			infoText = $"{count} recorded updates";
		else
			infoText = valid ? "No recording" : "No preview model";

		Tooltip = infoText;
		_timeline.Tooltip = $"{FormatTime( elapsed )} / {FormatTime( duration )}\n{infoText}";
	}

	private static string RecordedUpdateText( int current, int count, AnimGraphRecordedUpdateInfo info )
		=> $"Update {current + 1} / {count} · " +
			$"Sync {FormatSyncTime( info.StartEvent, info.StartPercentage )} → {FormatSyncTime( info.EndEvent, info.EndPercentage )} · " +
			$"{info.DeltaTime * 1000f:0.00} ms";

	private static string FormatSyncTime( int eventIndex, float percentage ) => $"{eventIndex}:{percentage:0.000}";

	private static string FormatTime( float seconds )
	{
		var time = TimeSpan.FromSeconds( Math.Max( 0f, seconds ) );
		return $"{(int)time.TotalMinutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
	}

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( !e.Pressed )
			return;

		switch ( e.Button )
		{
			case "space": TogglePlayback(); break;
			case "left": StepBackward(); break;
			case "right": StepForward(); break;
			case "home": JumpToStart(); break;
			case "end": JumpToEnd(); break;
			default:
				base.OnButtonTyped( e );
				return;
		}

		e.StopPropagation = true;
	}

	public override void Tick()
	{
		base.Tick();

		//A hidden panel still ticks, where a hidden widget did not. Without this, a recording left playing goes on stepping
		//the preview after the editor switches to the model half - where the transport that would stop it is not on screen.
		if ( HasClass( "hidden" ) ) return;

		AdvancePlayback();
		Refresh();
	}
}
