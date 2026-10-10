namespace Editor.Gallery;

/// <summary>One page of the gallery: the examples that share a [Category].</summary>
public sealed record GallerySection( string Title, string Icon, IReadOnlyList<GalleryEntry> Entries )
{
	public GalleryEntry Find( string id )
		=> Entries.FirstOrDefault( x => string.Equals( x.Id, id, StringComparison.OrdinalIgnoreCase ) );
}
