namespace Editor.SpriteEditor;

/// <summary>The sprite as it plays. The picture is <see cref="SpritePreviewPanel"/>.</summary>
public sealed class PreviewDock : SpriteDock
{
	private readonly SpritePreviewScene _scene;
	private readonly SpritePlayback _playback;
	private readonly IAnimationSelection _selection;

	public PreviewDock( SpritePreviewScene scene, SpritePlayback playback, IAnimationSelection selection )
		: base( "Preview", "emoji_emotions" )
	{
		_scene = scene;
		_playback = playback;
		_selection = selection;

		MinimumSize = new Vector2( 256, 256 );

		Rebuild();
	}

	protected override Rinten.UI.Panel CreatePanel()
		=> new SpritePreviewPanel { PreviewScene = _scene, Playback = _playback, Selection = _selection };
}
