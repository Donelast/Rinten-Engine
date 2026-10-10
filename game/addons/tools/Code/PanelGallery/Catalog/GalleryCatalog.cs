namespace Editor.Gallery;

//Nothing is cached: the window makes a new catalog on hotload, the self-test makes one of its own, and a test can make one out of a single hand-written source. Sources are added in CreateDefault and nowhere else.
/// <summary>Every example there is, from every source, arranged into pages.</summary>
public sealed class GalleryCatalog
{
	readonly IReadOnlyList<IGalleryEntrySource> sources;

	public GalleryCatalog( params IGalleryEntrySource[] sources )
	{
		this.sources = sources ?? [];
	}

	/// <summary>The catalog the editor shows: everything marked [PanelGallery], and a page per property editor.</summary>
	public static GalleryCatalog CreateDefault()
		=> new( new AttributeGallerySource(), new ControlGallerySource( SampleValues.CreateDefault() ) );

	public IReadOnlyList<GalleryEntry> Entries()
	{
		var list = new List<GalleryEntry>();

		foreach ( var source in sources )
		{
			try
			{
				list.AddRange( source.Entries() );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Panel Gallery: {source.GetType().Name} threw while listing its examples" );
			}
		}

		return list;
	}

	/// <summary>Pages in the order shown: by the lowest [Order] on the page, then by name. Examples on a page the same way.</summary>
	public IReadOnlyList<GallerySection> Sections()
	{
		return Entries()
			.GroupBy( x => x.Group )
			.OrderBy( g => g.Min( x => x.Order ) )
			.ThenBy( g => g.Key, StringComparer.OrdinalIgnoreCase )
			.Select( g => new GallerySection( g.Key, g.First().Icon,
				g.OrderBy( x => x.Order ).ThenBy( x => x.Title, StringComparer.OrdinalIgnoreCase ).ToList() ) )
			.ToList();
	}

	/// <summary>An example by its id, or by its title where nothing has that id. Null for neither.</summary>
	public GalleryEntry Find( string idOrTitle )
	{
		if ( string.IsNullOrWhiteSpace( idOrTitle ) ) return null;

		var entries = Entries();

		return entries.FirstOrDefault( x => string.Equals( x.Id, idOrTitle, StringComparison.OrdinalIgnoreCase ) )
			?? entries.FirstOrDefault( x => string.Equals( x.Title, idOrTitle, StringComparison.OrdinalIgnoreCase ) );
	}
}
