using Rinten.Tiles;

namespace Editor.Tiles;

/// <summary>The tile map tool's sidebar - see TileMapToolPanel.razor for the markup and TileMapToolPanel.razor.scss for the look.</summary>
[StyleSheet( "TileMapToolPanel.razor.scss" )]
public partial class TileMapToolPanel
{
	readonly TileMapTool tool;

	SerializedObject layerSheet;
	TileMapLayer layerSheetFor;

	public TileMapToolPanel( TileMapTool tool )
	{
		this.tool = tool;
	}

	//The painted layer's own settings, made again only when the layer changes.
	SerializedObject LayerSheet
	{
		get
		{
			var layer = TileMapTool.Layer;

			if ( !ReferenceEquals( layer, layerSheetFor ) )
			{
				layerSheetFor = layer;
				layerSheet = layer?.GetSerialized();
			}

			return layerSheet;
		}
	}

	List<(Asset Asset, TileSet Set)> sets;
	int setsFor = -1;

	//The project's tile sets, looked for again when the number of them changes.
	List<(Asset Asset, TileSet Set)> Sets
	{
		get
		{
			var count = TileSetAssetCount();

			if ( sets is null || count != setsFor )
			{
				sets = TileMapTool.ProjectTileSets();
				setsFor = count;
			}

			return sets;
		}
	}

	static int TileSetAssetCount() => AssetSystem.All.Count( a => a.AssetType?.FileExtension == "tileset" );

	//What a new map is made with: the one chosen, else the project's first.
	TileSet NewSet => TileMapTool.NewMapSet ?? Sets.Select( x => x.Set ).FirstOrDefault();

	void OpenTileSet( TileSet set ) => AssetSystem.FindByPath( set.ResourcePath )?.OpenInEditor();

	//What the brush in hand does, since several of them look alike until tried.
	string BrushHint => tool.CurrentTool switch
	{
		TilePaintBrush => "Paint: the left button lays the chosen tile, dragged for a line. Ctrl and the left button rub out.",
		TileRectangleBrush => "Rectangle: drag from corner to corner, filled when the button comes up. With Ctrl the rectangle is rubbed out.",
		TileFillBrush => "Fill: click to fill every joined cell that holds what the clicked one holds. With Ctrl they are emptied.",
		TileTerrainBrush when CurrentTerrain is { } terrain => $"Terrain: lays {terrain.Name}, and it and its neighbours pick their edges themselves. Ctrl and the left button rub out.",
		TileTerrainBrush => "Terrain: this tile set has no terrains yet - add one in the tile set to paint with this brush.",
		TilePickBrush => "Pick: click a tile on the map to paint with it.",
		_ => "Left button paints, Ctrl and left rubs out.",
	};

	static TileTerrain CurrentTerrain
	{
		get
		{
			var set = TileMapTool.Layer?.TileSet;
			return set?.Terrain( TileMapTool.TerrainName ) ?? set?.Terrains?.FirstOrDefault();
		}
	}

	void SelectLayer( int index ) => TileMapTool.LayerIndex = index;

	void PickTile( int tile ) => TileMapTool.Tile = tile;

	void AddLayer() => tool.AddLayer();

	bool IsTerrain( string name )
	{
		var set = TileMapTool.Layer?.TileSet;
		var chosen = set?.Terrain( TileMapTool.TerrainName ) ?? set?.Terrains?.FirstOrDefault();

		return chosen is not null && string.Equals( chosen.Name, name, StringComparison.OrdinalIgnoreCase );
	}

	protected override int BuildHash()
	{
		var map = TileMapTool.Map;
		var layer = TileMapTool.Layer;

		return HashCode.Combine(
			HashCode.Combine( map, map?.Layers?.Count, TileMapTool.LayerIndex, TileMapTool.Tile, TileMapTool.TerrainName ),
			HashCode.Combine( layer?.TileSet, layer?.TileSet?.Revision, layer?.Visible ),
			HashCode.Combine( tool.CurrentTool?.GetType(), tool.MadeByCode, TileMapTool.NewMapSet, TileSetAssetCount() ) );
	}
}
