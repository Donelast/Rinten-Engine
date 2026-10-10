namespace Editor.SpriteEditor;

/// <summary>Preview playback intent (animation, playing); the renderer keeps time and the current frame</summary>
public sealed class SpritePlayback : ISpritePlayback, IDisposable
{
	private readonly SpriteRenderer _renderer;
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;

	private bool _playing = true;

	public event Action Changed;

	public SpritePlayback( SpriteRenderer renderer, ISpriteDocument document, IAnimationSelection selection )
	{
		_renderer = renderer;
		_document = document;
		_selection = selection;

		_document.Loaded += Show;
		_document.Changed += OnChanged;
		_selection.Changed += Show;

		Show();
	}

	public void Dispose()
	{
		_document.Loaded -= Show;
		_document.Changed -= OnChanged;
		_selection.Changed -= Show;
	}

	public bool IsPlaying => _playing;

	public int FrameCount => _selection.Selected?.Frames?.Count ?? 0;

	public int CurrentFrame
	{
		get => _renderer.IsValid() ? _renderer.CurrentFrameIndex : 0;
		set
		{
			if ( !_renderer.IsValid() ) return;

			_renderer.CurrentFrameIndex = Math.Clamp( value, 0, Math.Max( 0, FrameCount - 1 ) );
		}
	}

	public void TogglePlayPause()
	{
		//Pressing play on the last frame of an animation that does not loop starts it over rather than playing nothing.
		if ( !_playing && _selection.Selected is { LoopMode: Sprite.LoopMode.None } && CurrentFrame >= FrameCount - 1 )
			CurrentFrame = 0;

		SetPlaying( !_playing );
	}

	public void GotoFirst() => CurrentFrame = 0;

	public void GotoLast() => CurrentFrame = FrameCount - 1;

	public void GotoNext()
	{
		var next = CurrentFrame + 1;
		CurrentFrame = next >= FrameCount ? 0 : next;
	}

	public void GotoPrevious()
	{
		var previous = CurrentFrame - 1;
		CurrentFrame = previous < 0 ? FrameCount - 1 : previous;
	}

	/// <summary>Once a frame, after the scene ticked: an animation that does not loop has stopped on its last frame by itself, and the play button follows it.</summary>
	public void Update()
	{
		if ( !_playing ) return;
		if ( !_renderer.IsValid() || !_renderer.IsFinished ) return;

		SetPlaying( false );
	}

	private void SetPlaying( bool playing )
	{
		if ( _playing == playing ) return;

		_playing = playing;

		if ( _renderer.IsValid() ) _renderer.PlaybackSpeed = playing ? 1 : 0;

		Changed?.Invoke();
	}

	private void OnChanged( SpriteChange change ) => Show();

	//Points the renderer at the sprite and the selected animation.
	private void Show()
	{
		if ( !_renderer.IsValid() ) return;

		_renderer.Sprite = _document.Sprite;

		if ( _selection.Selected is { } animation )
			_renderer.PlayAnimation( animation.Name );

		_renderer.PlaybackSpeed = _playing ? 1 : 0;
	}
}
