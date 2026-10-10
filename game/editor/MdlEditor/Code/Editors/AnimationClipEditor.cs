using System.Collections.Generic;
using Editor.MdlEditor.Nodes;
using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Clip node page: the clip on the target model, with its transport inside the preview's strip
[NodePage( typeof( AnimationClipNode ) )]
[StyleSheet( "AnimationClipEditor.cs.scss" )]
internal sealed class AnimationClipPage : PreviewScenePanel, INodePage
{
	private readonly Window _window;
	private AnimationClipNode _node;
	private readonly ClipPreview _preview = new();
	private readonly PanelToolbar _transport;

	//The transport buttons, so they can all be greyed out together.
	private readonly List<PanelToolbarButton> _buttons = new();
	private readonly PanelToolbarButton _playButton;
	private readonly ClipScrubber _scrubber;
	private readonly UILabel _timeLabel;
	private readonly UILabel _durationLabel;
	private readonly UILabel _hint;
	private bool _dirty = true;
	private bool _scrubbing;
	private bool _resumeAfterScrub;
	private Model _previewModel;
	private string _previewAnimation;
	private bool _previewLoop;
	private float _previewSpeed;

	public AnimationClipPage( AnimationClipNode node, Window window )
		: base( "Animation Clip Preview" )
	{
		_window = window;
		_node = node;

		_hint = AddChild<UILabel>( "clip-hint hidden" );
		_hint.Tokenize = false;

		_transport = TransportHost.AddChild<PanelToolbar>( "clip-transport" );

		_buttons.Add( _transport.AddIcon( "first_page", "Go to start", Restart ) );
		_buttons.Add( _transport.AddIcon( "skip_previous", "Previous frame", () => StepBy( -1 ) ) );

		_playButton = _transport.AddToggle( "play_arrow", "Play / pause", true, OnPlayToggled );
		_buttons.Add( _playButton );

		_buttons.Add( _transport.AddIcon( "skip_next", "Next frame", () => StepBy( 1 ) ) );
		_buttons.Add( _transport.AddIcon( "last_page", "Go to end", GoToEnd ) );

		_timeLabel = AddTimeLabel( "start" );

		_scrubber = _transport.AddChild<ClipScrubber>();
		_scrubber.ScrubStarted = BeginScrub;
		_scrubber.ScrubEnded = EndScrub;
		_scrubber.ValueEdited = Scrub;

		_durationLabel = AddTimeLabel( "end" );

		ShowTransport = true;
	}

	//One of the two readouts either side of the strip.
	private UILabel AddTimeLabel( string side )
	{
		var label = _transport.AddChild<UILabel>( $"time {side}" );

		label.Tokenize = false;
		label.Text = ClipScrubber.FormatTime( 0f );

		return label;
	}

	public bool Rebind( Node node )
	{
		if ( node is not AnimationClipNode clip || clip.Identifier != _node.Identifier )
			return false;

		_node = clip;
		return true;
	}

	private void Rebuild()
	{
		_dirty = false;

		var model = _window.Graph?.TargetModel;
		_previewModel = model;
		_previewAnimation = _node.AnimationName;
		_previewLoop = _node.Loop;
		_previewSpeed = _node.SpeedMultiplier;

		SetModel( model );

		if ( !Renderer.IsValid() )
		{
			_preview.Clear();
			ShowHint( "Set a target model to preview this animation." );
			return;
		}

		if ( string.IsNullOrWhiteSpace( _node.AnimationName ) )
		{
			_preview.Clear();
			ShowHint( "Select an animation on this node to preview it." );
			return;
		}

		_preview.Looping = _node.Loop;
		_preview.PlaybackRate = _node.SpeedMultiplier;
		_preview.Play( Renderer, _node.AnimationName );
		_preview.TimeNormalized = 0f;

		if ( _preview.Duration <= 0f )
		{
			ShowHint( "This animation isn't compiled for the target skeleton." );
			return;
		}

		_hint.SetClass( "hidden", true );
		SetTransportEnabled( true );
		SetPlaying( _playButton.IsActive );
	}

	//Why there is nothing to play, which is also why the transport is dead.
	private void ShowHint( string text )
	{
		_hint.Text = text;
		_hint.SetClass( "hidden", false );

		SetTransportEnabled( false );
	}

	private void SetTransportEnabled( bool enabled )
	{
		//On each button rather than on the bar: the bar's colour never reaches them, because the sheet gives a button its
		//own, so a class up here left them fully lit while nothing answered. Enabled dims and deafens the one it is set on.
		foreach ( var button in _buttons )
			button.Enabled = enabled;

		_scrubber.SetClass( "is-disabled", !enabled );

		if ( enabled )
			return;

		_scrubber.Value = 0f;
		_scrubber.Duration = 0f;
		_scrubber.FrameCount = 0;
		_timeLabel.Text = ClipScrubber.FormatTime( 0f );
		_durationLabel.Text = ClipScrubber.FormatTime( 0f );
	}

	private void UpdatePlaybackSettings()
	{
		if ( _previewLoop != _node.Loop )
		{
			_previewLoop = _node.Loop;
			_preview.Looping = _previewLoop;
		}

		if ( _previewSpeed != _node.SpeedMultiplier )
		{
			_previewSpeed = _node.SpeedMultiplier;
			_preview.PlaybackRate = _previewSpeed;
		}
	}

	//Puts the play button and the scene's clock in step. Setting the button does not fire its own toggle, so this can be called from anywhere.
	private void SetPlaying( bool playing )
	{
		_playButton.IsActive = playing;

		Scene.TimeScale = playing ? 1f : 0f;
	}

	private void OnPlayToggled( bool playing )
	{
		if ( playing && _preview.IsFinished )
			_preview.TimeNormalized = 0f;

		SetPlaying( playing );
	}

	private void Restart()
	{
		_preview.TimeNormalized = 0f;
	}

	private void GoToEnd()
	{
		SetPlaying( false );

		var duration = _preview.Duration;
		if ( duration <= 0f )
			return;

		_preview.Time = MathF.Max( duration - 1f / _preview.FrameRate, 0f );
	}

	private void StepBy( int direction )
	{
		SetPlaying( false );

		var duration = _preview.Duration;
		if ( duration <= 0f )
			return;

		var frameRate = _preview.FrameRate;
		var end = MathF.Max( duration - 1f / frameRate, 0f );
		_preview.Time = (_preview.Time + direction / frameRate).Clamp( 0f, end );
	}

	private void BeginScrub()
	{
		_scrubbing = true;
		_resumeAfterScrub = _playButton.IsActive;
		SetPlaying( false );
	}

	private void Scrub()
	{
		_preview.TimeNormalized = _scrubber.Value;
	}

	private void EndScrub()
	{
		_scrubbing = false;
		SetPlaying( _resumeAfterScrub );
	}

	private void UpdateTransport()
	{
		if ( !_preview.HasClip )
			return;

		var duration = _preview.Duration;
		var time = _preview.Time.Clamp( 0f, duration );

		_scrubber.Duration = duration;
		_scrubber.FrameCount = _preview.FrameCount;

		if ( !_scrubbing )
			_scrubber.Value = duration > 0f ? time / duration : 0f;

		_timeLabel.Text = ClipScrubber.FormatTime( time );
		_durationLabel.Text = ClipScrubber.FormatTime( duration );

		if ( _preview.IsFinished && _playButton.IsActive )
			SetPlaying( false );
	}

	protected override void OnScenePreFrame()
	{
		if ( _dirty
			|| _previewModel != _window.Graph?.TargetModel
			|| _previewAnimation != _node.AnimationName )
			Rebuild();
		else
			UpdatePlaybackSettings();

		ApplyPreviewSettings( _window.Graph );

		if ( _playButton.IsActive && !_scrubbing )
			_preview.Advance( RealTime.Delta );

		UpdateTransport();
	}

	//The clip's own root motion says exactly where the model has travelled to at any point in time, so the ground is placed
	//from that rather than integrated. Scrubbing jumps around, including backwards, which per-frame deltas can't describe.
	protected override void UpdateTreadmill()
	{
		if ( _preview.Clip is not { IsBound: true } clip )
		{
			Ground.Reset();
			return;
		}

		Ground.SetFromRootMotion( clip.GetRootMotion( _preview.TimeNormalized ) );
	}

	public override void OnDeleted()
	{
		_preview.Dispose();

		base.OnDeleted();
	}
}
