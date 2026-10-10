using System;

namespace Editor.TreeBrowser;

//Where every tile in the icon grid sits, worked out rather than laid out. The grid only builds the
//tiles the user can see, so it has to know the size and position of the ones it is not building.
readonly record struct GridMetrics( float TileWidth, float TileHeight, float Gap, int Columns )
{
	//The icon modes carry two lines of writing: the name, and the extension under it.
	/// <summary>Tile size per view mode; the height is what makes the rows countable.</summary>
	public static Vector2 TileSize( AssetListViewMode mode ) => mode switch
	{
		AssetListViewMode.List => new Vector2( 0, 28 ),
		AssetListViewMode.SmallIcons => new Vector2( 56, 72 ),
		AssetListViewMode.LargeIcons => new Vector2( 136, 152 ),
		_ => new Vector2( 88, 104 )
	};

	public static string ClassFor( AssetListViewMode mode ) => mode switch
	{
		AssetListViewMode.List => "list",
		AssetListViewMode.SmallIcons => "small",
		AssetListViewMode.LargeIcons => "large",
		_ => "medium"
	};

	/// <summary>Works out how the current view mode fits into a given width.</summary>
	public static GridMetrics For( AssetListViewMode mode, float width )
	{
		var size = TileSize( mode );

		if ( mode == AssetListViewMode.List )
			return new GridMetrics( width, size.y, 2f, 1 );

		const float gap = 8f;
		var columns = Math.Max( 1, (int)((width + gap) / (size.x + gap)) );

		return new GridMetrics( size.x, size.y, gap, columns );
	}

	/// <summary>Top of one row to the top of the next.</summary>
	public float Step => TileHeight + Gap;

	public int RowCount( int itemCount ) => Columns <= 0 ? 0 : (itemCount + Columns - 1) / Columns;

	public float TotalHeight( int itemCount )
	{
		var rows = RowCount( itemCount );
		return rows == 0 ? 0f : rows * Step - Gap;
	}

	public float LeftOf( int index ) => index % Columns * (TileWidth + Gap);

	public float TopOf( int index ) => index / Columns * Step;
}
