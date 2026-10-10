namespace Editor.SpriteEditor;

/// <summary>Where the preview is in the selected animation, and whether it is moving.</summary>
public interface ISpritePlayback
{
	bool IsPlaying { get; }

	/// <summary>The frame the preview is showing. Clamped to the animation.</summary>
	int CurrentFrame { get; set; }

	/// <summary>How many frames the selected animation has.</summary>
	int FrameCount { get; }

	/// <summary>Playing or paused flipped.</summary>
	event Action Changed;

	void TogglePlayPause();
	void GotoFirst();
	void GotoLast();
	void GotoNext();
	void GotoPrevious();
}
