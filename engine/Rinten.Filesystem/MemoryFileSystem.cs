namespace Rinten;

//A filesystem that only exists in memory
internal class MemoryFileSystem : BaseFileSystem
{
	internal MemoryFileSystem() : base( new Zio.FileSystems.MemoryFileSystem() )
	{

	}
}
