namespace Rinten;

public partial class SoundFile
{
	//Name of the custom resource block holding the subtitle words in a compiled sound. Injected at compile time
	//by the managed sound compiler from the subtitles authored in the sound editor (stored in the sound's .meta).
	internal const string SubtitleBlockName = "SUBT";

	/// <summary>Word-by-word subtitle track for this sound, or null if it has none.</summary>
	public SubtitleTrack Subtitles { get; private set; }

	//Read the subtitle track out of the sound's compiled file data. Called when the sound resource loads or reloads.
	void ReadSubtitles( ResourceLoadContext context )
	{
		var words = context.ReadJson<List<SubtitleTrack.Word>>( SubtitleBlockName );

		Subtitles = words is { Count: > 0 } ? new SubtitleTrack( words ) : null;
	}
}
