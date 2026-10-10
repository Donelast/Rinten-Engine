namespace Editor.SpriteEditor;

/// <summary>The frames of the selected animation, and the transport over them. The panel is <see cref="TimelinePanel"/>.</summary>
public sealed class TimelineDock : SpriteDock
{
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;
	private readonly ISpritePlayback _playback;
	private readonly FrameCommands _frames;
	private readonly FrameImporter _importer;

	internal TimelineDock( ISpriteDocument document, IAnimationSelection selection, ISpritePlayback playback, FrameCommands frames, FrameImporter importer )
		: base( "Timeline", "view_column" )
	{
		_document = document;
		_selection = selection;
		_playback = playback;
		_frames = frames;
		_importer = importer;

		MinimumWidth = 512f;
		MinimumHeight = 128f;

		Rebuild();
	}

	protected override Rinten.UI.Panel CreatePanel() => new TimelinePanel
	{
		Document = _document,
		Selection = _selection,
		Playback = _playback,
		Frames = _frames,
		Importer = _importer,
	};
}
