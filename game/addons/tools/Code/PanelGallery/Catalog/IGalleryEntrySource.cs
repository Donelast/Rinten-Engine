namespace Editor.Gallery;

/// <summary>Somewhere examples come from: the type library, a generated set, a list a test wrote by hand.</summary>
public interface IGalleryEntrySource
{
	IEnumerable<GalleryEntry> Entries();
}
