namespace Editor.Gallery;

/// <summary>How wide a card's stage is. Half the bugs in a control are what it does at 180 pixels.</summary>
public enum GalleryWidth
{
	[Title( "Narrow (180)" )] Narrow,
	[Title( "Inspector (320)" )] Inspector,
	[Title( "Wide (640)" )] Wide,
	[Title( "Fill" )] Fill,
}

//Plain properties, so the gallery's own inspector dock can be a ControlSheet over this object - the gallery eating its own cooking. Anything watching the page hears Changed and re-applies.
/// <summary>What every card on a page is shown under: width, read-only, how many copies.</summary>
public sealed class GalleryViewOptions
{
	public event Action Changed;

	GalleryWidth width = GalleryWidth.Fill;
	bool readOnly;
	int copies = 1;

	[Title( "Width" ), Description( "How wide each example's stage is." )]
	public GalleryWidth Width
	{
		get => width;
		set { if ( width == value ) return; width = value; Changed?.Invoke(); }
	}

	[Title( "Read Only" ), Description( "Every property editor on the page in its read-only state." )]
	public bool ReadOnly
	{
		get => readOnly;
		set { if ( readOnly == value ) return; readOnly = value; Changed?.Invoke(); }
	}

	[Title( "Copies" ), Description( "How many of each example to build, for weighing a page's frame cost." ), Range( 1, 16 )]
	public int Copies
	{
		get => copies;
		set { value = Math.Clamp( value, 1, 16 ); if ( copies == value ) return; copies = value; Changed?.Invoke(); }
	}

	/// <summary>The class a stage wears for the current width; the sheet knows the pixels.</summary>
	public string WidthClass => width switch
	{
		GalleryWidth.Narrow => "w-narrow",
		GalleryWidth.Inspector => "w-inspector",
		GalleryWidth.Wide => "w-wide",
		_ => "w-fill",
	};

	public static readonly string[] AllWidthClasses = ["w-narrow", "w-inspector", "w-wide", "w-fill"];
}
