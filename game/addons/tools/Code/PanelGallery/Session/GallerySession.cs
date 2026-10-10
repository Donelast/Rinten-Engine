namespace Editor.Gallery;

/// <summary>What a card can tell the inspector dock about the example standing in it.</summary>
public interface IGalleryReadout
{
	GalleryEntry Entry { get; }

	/// <summary>Panels under the example, itself included, over every copy.</summary>
	int PanelCount { get; }

	/// <summary>How long the constructors took, over every copy.</summary>
	double BuildMilliseconds { get; }

	/// <summary>How often the example's subtree is ticked, sampled over the last second.</summary>
	float TicksPerSecond { get; }

	/// <summary>What went wrong building it, or null.</summary>
	string Error { get; }

	/// <summary>What the window around the example spends per frame: layout, input, draw, and the frame rate.</summary>
	Rinten.UI.UiFrameCost WindowCost { get; }
}

/// <summary>Panel-free gallery state shared by all docks, so a test can drive it without a window</summary>
public sealed class GallerySession
{
	readonly Func<GalleryCatalog> makeCatalog;

	public GallerySession( Func<GalleryCatalog> makeCatalog )
	{
		this.makeCatalog = makeCatalog ?? throw new ArgumentNullException( nameof( makeCatalog ) );
	}

	public GalleryCatalog Catalog { get; private set; }
	public IReadOnlyList<GallerySection> Sections { get; private set; } = [];
	public GallerySection CurrentSection { get; private set; }
	public GalleryEntry SelectedEntry { get; private set; }

	/// <summary>The card showing the selected example, where a page has one.</summary>
	public IGalleryReadout SelectedReadout { get; private set; }

	public GalleryViewOptions Options { get; } = new();

	/// <summary>The catalog was rebuilt - after hotload, or on request.</summary>
	public event Action CatalogChanged;

	/// <summary>A different page is open. Null where there are none.</summary>
	public event Action<GallerySection> SectionChanged;

	/// <summary>A different example is picked, or none.</summary>
	public event Action<GalleryEntry> SelectionChanged;

	/// <summary>Somebody asked for every card on the page to be built again.</summary>
	public event Action PageRebuildRequested;

	/// <summary>Reads the catalog again and stays on the page of the same name where there still is one.</summary>
	public void Reload()
	{
		Catalog = makeCatalog();
		Sections = Catalog.Sections();

		var keep = CurrentSection?.Title;

		CurrentSection = Sections.FirstOrDefault( x => x.Title == keep ) ?? Sections.FirstOrDefault();
		SelectedEntry = null;
		SelectedReadout = null;

		CatalogChanged?.Invoke();
		SectionChanged?.Invoke( CurrentSection );
		SelectionChanged?.Invoke( null );
	}

	public void ShowSection( GallerySection section )
	{
		if ( section is null || section == CurrentSection ) return;

		CurrentSection = section;
		SelectedEntry = null;
		SelectedReadout = null;

		SectionChanged?.Invoke( section );
		SelectionChanged?.Invoke( null );
	}

	/// <summary>Opens the page an example is on and picks it. False where nothing goes by that name.</summary>
	public bool Show( string idOrTitle )
	{
		var entry = Catalog?.Find( idOrTitle );
		if ( entry is null ) return false;

		var section = Sections.FirstOrDefault( s => s.Find( entry.Id ) is not null );
		if ( section is null ) return false;

		ShowSection( section );
		Select( section.Find( entry.Id ) );

		return true;
	}

	public void Select( GalleryEntry entry )
	{
		if ( entry == SelectedEntry ) return;

		SelectedEntry = entry;
		SelectedReadout = null;

		SelectionChanged?.Invoke( entry );
	}

	/// <summary>The page says which card is the selected one; the inspector reads it.</summary>
	public void SetReadout( IGalleryReadout readout )
	{
		SelectedReadout = readout is not null && readout.Entry == SelectedEntry ? readout : null;
	}

	public void RequestPageRebuild() => PageRebuildRequested?.Invoke();
}
