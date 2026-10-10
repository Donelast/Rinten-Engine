using Rinten.MovieMaker;
using System.Collections.Generic;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

//How a curve block is cut into tiles, and when each of them is repainted.
partial class CurveBlockItem<T>
{
	private readonly List<CurveTilePanel> _tiles = new();
	private bool _rangesDirty;

	//Make sure we have the right number of tiles given the current width of this block.
	private void UpdateTiles()
	{
		var width = SceneRect.Width;

		//Round pixelsPerSecond to a power of 2 so that we don't need to resize each tile every frame while smoothly zooming

		var pixelsPerSecond = Timeline.PixelsPerSecond.NearestPowerOfTwo();
		var scale = Timeline.PixelsPerSecond / pixelsPerSecond;

		var tileWidth = CurveTilePanel.TileWidth * scale;
		var tileCount = (int)MathF.Ceiling( width / tileWidth );
		var tileDurationSeconds = (double)CurveTilePanel.TileWidth / pixelsPerSecond;

		//Truncate if we have too many, or they're invalid

		while ( _tiles.Count > tileCount || _tiles.Count > 0 && !_tiles[^1].IsValid() )
		{
			_tiles.Pop().Delete();
		}

		//Append if we have too few

		while ( _tiles.Count < tileCount )
		{
			_tiles.Add( AddChild( new CurveTilePanel( this, _tiles.Count ) ) );
		}

		var prev = TimeRange.Start;

		foreach ( var tile in _tiles )
		{
			var next = TimeRange.Start + MovieTime.FromSeconds( (tile.Index + 1) * tileDurationSeconds );

			tile.Place( tileWidth * tile.Index, tileWidth );
			tile.TimeRange = (prev, next);

			prev = next;
		}
	}

	protected override void OnBlockChanged( MovieTimeRange timeRange )
	{
		UpdateTiles();

		foreach ( var tile in _tiles )
		{
			if ( tile.TimeRange.Intersect( timeRange ) is { IsEmpty: false } )
			{
				tile.MarkDirty( timeRange );
			}
		}

		_rangesDirty = true;
	}

	protected override void OnResize() => UpdateTiles();
}
