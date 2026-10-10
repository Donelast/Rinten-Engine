using Rinten.Tiles;

namespace Editor.Tiles;

/// <summary>A tile set, opened - see TileSetEditorPanel.razor for the markup and TileSetEditorPanel.razor.scss for the look. Every change goes through <see cref="Changed"/>, which tells maps using the set to draw again and the asset that it has something to save.</summary>
[StyleSheet( "TileSetEditorPanel.razor.scss" )]
public partial class TileSetEditorPanel
{
	readonly TileSet set;
	readonly Action noteChanged;
	readonly SerializedObject sheetProperties;

	int selected;
	int terrain = -1;

	SerializedObject infoSheet;
	TileInfo infoSheetFor;

	SerializedObject terrainSheet;
	TileTerrain terrainSheetFor;

	public TileSetEditorPanel( TileSet set, Action noteChanged )
	{
		this.set = set;
		this.noteChanged = noteChanged;

		sheetProperties = set.GetSerialized();
		sheetProperties.OnPropertyChanged += _ => Changed();

		if ( set.Terrains?.Count > 0 ) terrain = 0;
	}

	bool IsSheetProperty( SerializedProperty property )
		=> property.Name is nameof( TileSet.Sheet ) or nameof( TileSet.TileSize ) or nameof( TileSet.Margin ) or nameof( TileSet.Spacing );

	string Summary => set.Sheet is null
		? "Pick the picture the tiles are cut from."
		: $"{set.Columns} x {set.Rows} tiles of {set.TileSize.x} x {set.TileSize.y} pixels, from a {set.Sheet.Width} x {set.Sheet.Height} sheet.";

	void Select( int tile ) => selected = tile;

	TileCollision Collision => set.CollisionOf( selected );

	void SetCollision( TileCollision collision )
	{
		if ( selected <= 0 ) return;

		set.InfoOrNew( selected ).Collision = collision;
		Changed();
	}

	void Describe()
	{
		if ( selected <= 0 ) return;

		set.InfoOrNew( selected );
		Changed();
	}

	//The picked tile's own settings, where the set says anything about it.
	SerializedObject InfoSheet
	{
		get
		{
			var info = set.Info( selected );

			if ( !ReferenceEquals( info, infoSheetFor ) )
			{
				infoSheetFor = info;
				infoSheet = info?.GetSerialized();
				if ( infoSheet is not null ) infoSheet.OnPropertyChanged += _ => Changed();
			}

			return infoSheet;
		}
	}

	TileTerrain Terrain => set.Terrains is not null && terrain >= 0 && terrain < set.Terrains.Count ? set.Terrains[terrain] : null;

	SerializedObject TerrainSheet
	{
		get
		{
			var chosen = Terrain;

			if ( !ReferenceEquals( chosen, terrainSheetFor ) )
			{
				terrainSheetFor = chosen;
				terrainSheet = chosen?.GetSerialized();
				if ( terrainSheet is not null ) terrainSheet.OnPropertyChanged += _ => Changed();
			}

			return terrainSheet;
		}
	}

	void AddTerrain()
	{
		( set.Terrains ??= new() ).Add( new TileTerrain { Name = $"Terrain {set.Terrains.Count + 1}" } );
		terrain = set.Terrains.Count - 1;
		Changed();
	}

	bool IsTerrainProperty( SerializedProperty property ) => property.Name != nameof( TileTerrain.ByMask );

	void RemoveTerrain( int index, PanelEvent e )
	{
		e.StopPropagation();

		if ( set.Terrains is null || index < 0 || index >= set.Terrains.Count ) return;

		set.Terrains.RemoveAt( index );
		terrain = Math.Min( terrain, set.Terrains.Count - 1 );
		Changed();
	}

	//Give one of the sixteen the picked tile.
	void Assign( int mask )
	{
		if ( Terrain is not { } chosen || selected <= 0 ) return;

		SetSlot( chosen, mask, selected );
	}

	void Clear( int mask )
	{
		if ( Terrain is { } chosen ) SetSlot( chosen, mask, 0 );
	}

	void SetSlot( TileTerrain chosen, int mask, int tile )
	{
		chosen.ByMask ??= new();
		while ( chosen.ByMask.Count < 16 ) chosen.ByMask.Add( 0 );

		chosen.ByMask[mask] = tile;
		Changed();
	}

	//Which of the sixteen sits in a place of the four-by-four: rows go no neighbour, below, above and below, above - so the top row is a strip, the rows
	//under it the top, middle and bottom of a block - and columns the same across. Laid out so, the slots are the pieces put together, as a sheet draws them.
	static int SlotMask( int row, int column )
	{
		int[] rows = [0, TilePainter.Below, TilePainter.Above | TilePainter.Below, TilePainter.Above];
		int[] columns = [0, TilePainter.Right, TilePainter.Left | TilePainter.Right, TilePainter.Left];

		return rows[row] | columns[column];
	}

	//A shape painted with the terrain, so every way its tiles meet shows at once.
	static readonly string[] PreviewShape =
	[
		"#.###.#..#.",
		"......#.###",
		"#.....#..#.",
		"...........",
		".#####..##.",
		".##.##.####",
		".#####..#..",
	];

	//The tile the terrain gives a cell of the preview: nought for empty ground, below nought where its slot is empty.
	static int PreviewTile( TileTerrain chosen, int x, int y )
	{
		bool Filled( int px, int py ) => py >= 0 && py < PreviewShape.Length && px >= 0 && px < PreviewShape[py].Length && PreviewShape[py][px] == '#';

		if ( !Filled( x, y ) ) return 0;

		var mask = ( Filled( x, y - 1 ) ? TilePainter.Above : 0 ) | ( Filled( x + 1, y ) ? TilePainter.Right : 0 )
			| ( Filled( x, y + 1 ) ? TilePainter.Below : 0 ) | ( Filled( x - 1, y ) ? TilePainter.Left : 0 );

		var tile = chosen.Tile( mask );
		return tile > 0 ? tile : -1;
	}

	static int MissingSlots( TileTerrain chosen )
	{
		var missing = 0;
		for ( var mask = 0; mask < 16; mask++ ) if ( chosen.Tile( mask ) <= 0 ) missing++;
		return missing;
	}

	//One tile of the sheet at a size, cropped out by where the background sits.
	string TileStyle( int tile, float cell )
	{
		if ( tile <= 0 || set.Sheet is null || !set.Has( tile ) ) return "";

		var region = set.Region( tile );
		var scale = cell / MathF.Max( region.Width, 1 );

		return string.Create( System.Globalization.CultureInfo.InvariantCulture,
			$"background-image: url({set.Sheet.ResourcePath}); " +
			$"background-size: {set.Sheet.Width * scale:0.##}px {set.Sheet.Height * scale:0.##}px; " +
			$"background-position: {-region.Left * scale:0.##}px {-region.Top * scale:0.##}px;" );
	}

	void Changed()
	{
		set.Changed();
		noteChanged?.Invoke();
		StateHasChanged();
	}

	protected override int BuildHash() => HashCode.Combine( set.Revision, selected, terrain, set.Sheet );
}
