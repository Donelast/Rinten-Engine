namespace Rinten;

//Audio is read from the source file at load, so replacing the file replaces the sound
/// <summary>A sound, as the project holds it: a file the artist exported and what to do with it.</summary>
[AssetType( Name = "Sound", Extension = "snd", Category = "Audio", IconColor = "#7fd68f", Icon = "audio_file" )]
public sealed class SoundDefinition : GameResource
{
	/// <summary>The audio file this plays. Left where the artist put it and read when the sound is first played.</summary>
	[Property, FilePath( "wav,mp3,flac,ogg" )]
	public string Source { get; set; } = "";

	//A property of the sound rather than of the playing: a fire loops and a gunshot does not, whoever is playing them.
	/// <summary>Whether it plays round and round until something stops it.</summary>
	[Property]
	public bool Looping { get; set; }

	//Zero with LoopEnd also zero means whatever the file marks as its loop - a WAV's sampler or cue chunk - and the whole sound when it marks none. A sound can have an intro before its loop this way, and a tail after it that plays once the loop is let go of.
	/// <summary>Where a loop goes back to, in seconds from the start.</summary>
	[Property, ShowIf( nameof( Looping ), true ), Title( "Loop Start (s)" )]
	public float LoopStart { get; set; }

	/// <summary>Where a loop ends, in seconds from the start; zero for the end of the sound.</summary>
	[Property, ShowIf( nameof( Looping ), true ), Title( "Loop End (s)" )]
	public float LoopEnd { get; set; }

	//Here rather than only on the event that plays it, because a recording that came back too quiet is a fact about the file, and fixing it once is better than remembering to fix it at every place it is used.
	/// <summary>How loud it is compared with the others, in decibels.</summary>
	[Property, Range( -24.0f, 24.0f )]
	public float Decibels { get; set; }
}
