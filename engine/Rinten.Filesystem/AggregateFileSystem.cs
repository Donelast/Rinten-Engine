namespace Rinten;

//A filesystem that merges a bunch of other filesystems. This is read only.
internal class AggregateFileSystem : BaseFileSystem
{
	internal AggregateFileSystem() : base( new Zio.FileSystems.AggregateFileSystem( false ) )
	{

	}

	public void UnMountAll()
	{
		(system as Zio.FileSystems.AggregateFileSystem).ClearFileSystems();
	}
}
