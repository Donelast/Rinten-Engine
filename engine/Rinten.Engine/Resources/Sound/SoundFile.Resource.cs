namespace Rinten;

public partial class SoundFile
{
	//The sound's file data has been loaded (or reloaded) - read what we want out of the compiled file.
	internal override void OnLoaded( ResourceLoadContext context )
	{
		ReadVisemes( context );
		ReadSubtitles( context );
	}
}
