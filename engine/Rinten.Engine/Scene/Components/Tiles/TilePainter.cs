namespace Rinten.Tiles;

/// <summary>Tile painting: lines, rects, floods and auto-terrains, shared by editor and runtime code</summary>
public static class TilePainter
{
	/// <summary>The four sides, as bits of a neighbour mask.</summary>
	public const int Above = 1;
	public const int Right = 2;
	public const int Below = 4;
	public const int Left = 8;

	private static readonly Vector2Int[] Sides = [new( 0, 1 ), new( 1, 0 ), new( 0, -1 ), new( -1, 0 )];

	/// <summary>The rectangle of cells between two corners, both included, either way round.</summary>
	public static RectInt Between( Vector2Int a, Vector2Int b )
		=> new( Math.Min( a.x, b.x ), Math.Min( a.y, b.y ), Math.Abs( a.x - b.x ) + 1, Math.Abs( a.y - b.y ) + 1 );

	/// <summary>Fill the rectangle between two corners.</summary>
	public static void Rect( TileMapLayer layer, Vector2Int from, Vector2Int to, int tile )
		=> layer?.Fill( Between( from, to ), tile );

	/// <summary>Every cell on the line between two cells - a drag across the screen skips cells, and the gaps show.</summary>
	public static IEnumerable<Vector2Int> Line( Vector2Int from, Vector2Int to )
	{
		var dx = Math.Abs( to.x - from.x );
		var dy = -Math.Abs( to.y - from.y );
		var stepX = from.x < to.x ? 1 : -1;
		var stepY = from.y < to.y ? 1 : -1;
		var error = dx + dy;

		var at = from;

		while ( true )
		{
			yield return at;

			if ( at == to ) yield break;

			var doubled = error * 2;

			if ( doubled >= dy )
			{
				error += dy;
				at = new Vector2Int( at.x + stepX, at.y );
			}

			if ( doubled <= dx )
			{
				error += dx;
				at = new Vector2Int( at.x, at.y + stepY );
			}
		}
	}

	/// <summary>Put a tile on every cell of the line between two cells.</summary>
	public static void Line( TileMapLayer layer, Vector2Int from, Vector2Int to, int tile )
	{
		if ( layer is null ) return;

		foreach ( var cell in Line( from, to ) ) layer.Set( cell, tile );
	}

	/// <summary>Fill everything joined to this cell that holds what it holds. Stops at a limit, because a flood on an unbounded grid over nothing never ends.</summary>
	public static void Flood( TileMapLayer layer, Vector2Int cell, int tile, int limit = 4096 )
	{
		if ( layer is null ) return;

		var was = layer.GetTile( cell );
		if ( was == tile ) return;

		var seen = new HashSet<Vector2Int> { cell };
		var queue = new Queue<Vector2Int>();
		queue.Enqueue( cell );

		var filled = 0;

		while ( queue.Count > 0 && filled < limit )
		{
			var at = queue.Dequeue();
			if ( layer.GetTile( at ) != was ) continue;

			layer.Set( at, tile );
			filled++;

			foreach ( var side in Sides )
			{
				if ( seen.Add( at + side ) ) queue.Enqueue( at + side );
			}
		}
	}

	/// <summary>Put a terrain in a cell, and let it and its neighbours pick the pictures that match how they now meet.</summary>
	public static void Terrain( TileMapLayer layer, Vector2Int cell, TileTerrain terrain )
	{
		if ( layer is null || terrain is null ) return;

		layer.Set( cell, terrain.Tile( 0 ) );

		Fit( layer, cell, terrain );

		foreach ( var side in Sides ) Fit( layer, cell + side, terrain );
	}

	/// <summary>The same, by the terrain's name in the layer's tile set.</summary>
	public static void Terrain( TileMapLayer layer, Vector2Int cell, string terrain )
		=> Terrain( layer, cell, layer?.TileSet?.Terrain( terrain ) );

	/// <summary>Take a terrain out of a cell, and let what is left fit itself again.</summary>
	public static void EraseTerrain( TileMapLayer layer, Vector2Int cell, TileTerrain terrain )
	{
		if ( layer is null || terrain is null ) return;

		layer.Erase( cell );

		foreach ( var side in Sides ) Fit( layer, cell + side, terrain );
	}

	/// <summary>Give a cell the picture that matches its neighbours, if it holds this terrain at all.</summary>
	public static void Fit( TileMapLayer layer, Vector2Int cell, TileTerrain terrain )
	{
		if ( layer is null || terrain is null ) return;

		var here = layer.GetTile( cell );
		if ( !terrain.Contains( here ) ) return;

		var wanted = terrain.Tile( MaskAt( layer, cell, terrain ) );
		if ( wanted > 0 && wanted != here ) layer.Set( cell, wanted );
	}

	/// <summary>Fit every cell of a terrain in this rectangle - after a generator filled it in one go.</summary>
	public static void FitArea( TileMapLayer layer, RectInt area, TileTerrain terrain )
	{
		for ( var y = area.Top; y < area.Top + area.Height; y++ )
		{
			for ( var x = area.Left; x < area.Left + area.Width; x++ )
			{
				Fit( layer, new Vector2Int( x, y ), terrain );
			}
		}
	}

	/// <summary>Which sides of a cell are the same terrain: one above, two right, four below, eight left.</summary>
	public static int MaskAt( TileMapLayer layer, Vector2Int cell, TileTerrain terrain )
	{
		var mask = 0;

		if ( terrain.Contains( layer.GetTile( cell + Sides[0] ) ) ) mask |= Above;
		if ( terrain.Contains( layer.GetTile( cell + Sides[1] ) ) ) mask |= Right;
		if ( terrain.Contains( layer.GetTile( cell + Sides[2] ) ) ) mask |= Below;
		if ( terrain.Contains( layer.GetTile( cell + Sides[3] ) ) ) mask |= Left;

		return mask;
	}
}
