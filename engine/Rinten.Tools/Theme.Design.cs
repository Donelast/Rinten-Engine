namespace Editor;

public static partial class Theme
{
	//Design tokens for painted UI, matching _tokens.scss; greys show state, colour is for meaning

	/// <summary>Grey ground for busy panels (inspector, folder tree); contents step down towards the page</summary>
	public static Color Pane => SurfaceBackground;

	/// <summary>A card: a badge, a section, a slot on a pane. A step up from whatever it lies on, and no more - a card that is much lighter than its pane reads as a second window inside the first.</summary>
	public static Color Tile => SurfaceBackground;

	public static Color TileHover => SurfaceLightBackground;

	public static Color TileDown => Base;

	/// <summary>The wash behind the thing being pointed at: a row in the asset browser, a menu option, a bare button. Still white with an alpha, because it lies over greys of several darknesses and has to read the same on all of them.</summary>
	public static Color Veil => Color.White.WithAlpha( 0.05f );

	public static Color VeilHover => Color.White.WithAlpha( 0.08f );

	public static Color VeilDown => Color.White.WithAlpha( 0.12f );

	/// <summary>Nothing: what a button is before the pointer reaches it.</summary>
	public static Color Quiet => Color.Transparent;

	/// <summary>A hairline. Every border in the editor is this one: a grey of its own rather than a wash of white, so an edge is the same line whatever darkness it happens to lie on.</summary>
	public static Color Edge => Border;

	/// <summary>Fainter still: a rule under a heading, a divider in a bar.</summary>
	public static Color EdgeSoft => Highlight;

	/// <summary>A hairline that means something: hovered, selected, picked up.</summary>
	public static Color EdgeLit => BorderLight;

	/// <summary>What the keyboard is pointed at. The accent, because being typed into is the one state worth a colour - everything else in here is said with grey.</summary>
	public static Color EdgeFocus => Primary;

	/// <summary>Fill for pressable things (button, tab, header), same as $slab; not for grounds</summary>
	public static Color Slab => ButtonBackground;

	public static Color SlabHover => SurfaceBackground;

	public static Color SlabDown => Base;

	//Down at the page's own darkness rather than up at the plate's. That is the whole of how a field is told from a button at a glance: one is a hole in the panel and the other is sitting on it.
	/// <summary>A field or a well: something typed into, dropped into, scrolled through.</summary>
	public static Color Sunken => ControlBackground;

	/// <summary>What a popup is made of, where it is painted rather than styled.</summary>
	public static Color Glass => WidgetBackground;

	/// <summary>The same, darker - for when there is a lot of text on it.</summary>
	public static Color GlassDeep => WindowBackground;

	/// <summary>Text. Brightness rather than colour: four steps and no more.</summary>
	public static Color Ink => Text;

	public static Color InkDim => Color.White.WithAlpha( 0.7f );

	public static Color InkFaint => TextLight;

	public static Color InkOff => TextDisabled;

	//On state is a lighter grey plus an accent bar; colour is reserved for meaning

	/// <summary>The fill of something that is on: a lit toolbar button, an open tab.</summary>
	public static Color OnFill => SurfaceBackground;

	/// <summary>The bar of accent beside it, which is what says so from across the window.</summary>
	public static Color OnEdge => Primary;

	//The picked plate - the same green-grey the stylesheet lists select with ($list-row-selected), dark and grey enough that a browser with fifty files picked is still one that can be read.
	/// <summary>The fill of a row that is selected in a list.</summary>
	public static Color SelectedFill => SelectedBackground;

	/// <summary>How round the top of a tab is: a dock's tab, a document's, a scene's.</summary>
	public static float TabRadius => ControlRadius * 1.5f;

	/// <summary>Yes: installed, confirmed, somewhere this can be dropped.</summary>
	public static Color Good => Green;

	/// <summary>No: an error, a refusal, somewhere this cannot go.</summary>
	public static Color Bad => Red;

	/// <summary>Careful: a warning, something unsaved, something costly.</summary>
	public static Color Careful => Yellow;

	/// <summary>What this one is, out of everything on the screen.</summary>
	public static Color Accent => Primary;
}
