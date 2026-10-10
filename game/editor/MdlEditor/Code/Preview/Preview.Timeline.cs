using Editor.Inspectors;
using Rinten.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using UILabel = Rinten.UI.Label;
using UIOption = Rinten.UI.Option;

namespace Editor.MdlEditor;

public partial class Preview
{
	ControlDropDown _clipPicker;
	UILabel _timeLabel;
	UILabel _frameLabel;
	PanelToolbarButton _playButton;
	PanelToolbarButton _loopButton;
	bool _fillingClips;
	readonly HashSet<string> _failedClipCompile = new( StringComparer.OrdinalIgnoreCase );

	//What the labels last said, so they are written only when it changes.
	int _shownFrame = -1;
	int _shownMilliseconds = -1;

	//The transport: which clip, and the buttons that play it. Not on the picture any more. The model editor's Timeline dock stands it over the clip's
	//timeline - see ModelTimeline - and here it is only made, and kept up to date. The graph editor's preview makes one too and shows it nowhere.
	internal Panel TransportBar { get; private set; }

	//The clip being played, for the timeline to read and to move.
	internal ClipPreview Playback => _clipPreview;

	ModelDefinition _pickedClipOf;
	ModelDefinition.ModelClip _pickedClip;

	//The definition's own entry for the clip the picker shows, whose events the timeline draws.
	internal ModelDefinition.ModelClip PickedClip
	{
		get
		{
			var name = PickedClipName;
			var definition = _host.ModelDefinition;

			if ( string.IsNullOrWhiteSpace( name ) || definition is null ) return null;

			if ( _pickedClip is not null && ReferenceEquals( _pickedClipOf, definition )
				&& string.Equals( _pickedClip.SequenceName, name, StringComparison.OrdinalIgnoreCase ) )
				return _pickedClip;

			_pickedClipOf = definition;
			_pickedClip = definition.AllClips()?.FirstOrDefault( c =>
				c is not null && string.Equals( c.SequenceName, name, StringComparison.OrdinalIgnoreCase ) );

			return _pickedClip;
		}
	}

	//A scrub on the timeline has begun: what is playing stops, as it does for a step.
	internal void PauseForScrub() => SetPaused( true );

	//Which clip the picker is showing, by the name the model knows it as.
	internal string PickedClipName
	{
		get => _clipPicker?.Value as string;
		set
		{
			if ( _clipPicker.IsValid() ) _clipPicker.Value = value ?? "";
		}
	}

	void BuildTimeline()
	{
		var bar = TransportBar = new Panel();
		bar.AddClass( "transport-bar" );

		_clipPicker = bar.AddChild<ControlDropDown>( "clips" );
		_clipPicker.Searchable = true;
		_clipPicker.Tooltip = "Animation clip";
		_clipPicker.ValueChanged = OnClipPicked;

		var buttons = bar.AddChild<PanelToolbar>();

		buttons.AddIcon( "first_page", "Go to start", () => _clipPreview.TimeNormalized = 0f );
		buttons.AddIcon( "skip_previous", "Previous frame", () => StepClip( -1 ) );

		_playButton = buttons.AddToggle( "play_arrow", "Play / pause", !_paused, playing => SetPaused( !playing ) );

		buttons.AddIcon( "skip_next", "Next frame", () => StepClip( 1 ) );
		buttons.AddIcon( "last_page", "Go to end", () =>
		{
			SetPaused( true );
			if ( _clipPreview.Duration > 0f )
				_clipPreview.Time = MathF.Max( _clipPreview.Duration - 1f / _clipPreview.FrameRate, 0f );
		} );

		_loopButton = buttons.AddToggle( "repeat", "Loop clip", _clipPreview.Looping, looping => _clipPreview.Looping = looping );

		_timeLabel = bar.AddChild<UILabel>( "time" );
		_timeLabel.Tooltip = "Time / length";

		_frameLabel = bar.AddChild<UILabel>( "frame" );
		_frameLabel.Tooltip = "Frame / last frame";
	}

	void OnClipPicked( string sequence )
	{
		if ( _fillingClips ) return;

		PlayNamedClip( sequence );
	}

	internal void FillClipList( ModelDefinition definition )
	{
		if ( !_clipPicker.IsValid() ) return;

		var current = PickedClipName;

		_fillingClips = true;

		//"(none)" is the empty value under a name, so nothing selected and nothing playing are the same thing to everything downstream.
		var options = new List<UIOption> { new( "(none)", "" ) };

		if ( definition is not null )
		{
			foreach ( var clip in definition.AllClips() )
			{
				if ( clip is null || !clip.Enabled ) continue;
				options.Add( new UIOption( clip.SequenceName, clip.SequenceName ) );
			}
		}

		_clipPicker.Options = options;
		_clipPicker.Value = options.Any( o => (string)o.Value == (current ?? "") ) ? current ?? "" : "";

		_fillingClips = false;

		if ( !_clipPreview.HasClip && !string.IsNullOrWhiteSpace( current ) )
			PlayNamedClip( current );
	}

	internal void PlayNamedClip( string sequence )
	{
		if ( string.IsNullOrWhiteSpace( sequence ) || sequence == "(none)" )
		{
			StopClip();
			return;
		}

		var definition = _host.ModelDefinition;
		var clip = definition?.AllClips()?.FirstOrDefault( c =>
			string.Equals( c.SequenceName, sequence, StringComparison.OrdinalIgnoreCase ) );

		PlayModelClip( clip );
	}

	internal void PlayModelClip( ModelDefinition.ModelClip clip )
	{
		if ( clip is null || string.IsNullOrWhiteSpace( clip.SequenceName ) )
		{
			StopClip();
			return;
		}

		//A clip whose File already is the compiled clip - a character with no source art - names where it actually
		//sits, which is not always beside the model: a ported character's clips live in a folder of their own.
		string path;

		if ( clip.File.EndsWith( ".anim_c", StringComparison.OrdinalIgnoreCase ) )
		{
			path = clip.File;
		}
		else
		{
			var dir = Path.GetDirectoryName( _host.ModelAsset?.Path ?? "" )?.Replace( '\\', '/' ) ?? "";
			path = string.IsNullOrWhiteSpace( dir )
				? clip.SequenceName + ".anim_c"
				: $"{dir}/{clip.SequenceName}.anim_c";
		}

		EnsureClipCompiled( clip, path );

		_clipPreview.Looping = clip.Looping;
		if ( _loopButton is not null )
			_loopButton.IsActive = clip.Looping;
		PlayClip( path );

		if ( !_fillingClips )
			PickedClipName = clip.SequenceName;
	}

	void EnsureClipCompiled( ModelDefinition.ModelClip clip, string resourcePath )
	{
		if ( AnimationClip.Load( resourcePath ) is not null )
		{
			_failedClipCompile.Remove( resourcePath );
			return;
		}

		if ( !_failedClipCompile.Add( resourcePath ) )
			return;

		var definition = _host.ModelDefinition;
		var onDisk = _host.ModelAsset?.AbsolutePath;
		var sourceDir = Path.GetDirectoryName( onDisk );
		if ( definition is null || string.IsNullOrWhiteSpace( sourceDir ) )
			return;

		var output = Path.Combine( sourceDir, clip.SequenceName + ".anim_c" );
		AnimationClip.Forget( resourcePath );
		if ( definition.TryCompileClip( clip, sourceDir, output ) )
			_failedClipCompile.Remove( resourcePath );
		else
			Log.Warning( $"Model: could not compile clip {clip.SequenceName}" );
	}

	void StepClip( int direction )
	{
		SetPaused( true );
		if ( !_clipPreview.HasClip || _clipPreview.FrameRate <= 0f ) return;
		_clipPreview.Time += direction / _clipPreview.FrameRate;
	}

	void TickTimeline()
	{
		if ( TransportBar is null ) return;

		if ( _playButton is not null )
			_playButton.IsActive = !_paused;

		if ( _loopButton is not null )
			_loopButton.IsActive = _clipPreview.Looping;

		var hasClip = _clipPreview.HasClip;
		var frame = hasClip ? (int)MathF.Round( _clipPreview.Time * _clipPreview.FrameRate ) : 0;
		var milliseconds = hasClip ? (int)( _clipPreview.Time * 1000f ) : 0;

		//Written only when it changes: a label's text is a string made, and this runs every frame of the editor's life.
		if ( frame == _shownFrame && milliseconds == _shownMilliseconds ) return;

		_shownFrame = frame;
		_shownMilliseconds = milliseconds;

		if ( !hasClip )
		{
			_timeLabel.Text = "-";
			_frameLabel.Text = "-";
			return;
		}

		var last = (int)MathF.Round( _clipPreview.Duration * _clipPreview.FrameRate );

		_timeLabel.Text = $"{ClipScrubber.FormatTime( _clipPreview.Time )} / {ClipScrubber.FormatTime( _clipPreview.Duration )}";
		_frameLabel.Text = $"{frame} / {last}";
	}
}
