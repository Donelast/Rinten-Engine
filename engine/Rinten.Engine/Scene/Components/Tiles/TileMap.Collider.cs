namespace Rinten.Tiles;

public sealed partial class TileMap
{
	//The solid tiles of every layer that collides, as boxes: neighbouring cells merged into the fewest rectangles that cover them. A floor
	//of two hundred tiles is one box - less for the solver, and nothing for a sliding body to catch on at the seams between separate ones.
	protected override IEnumerable<PhysicsShape> CreatePhysicsShapes( PhysicsBody targetBody, Transform local )
	{
		var size = CellSize;
		var scale = WorldScale;

		foreach ( var layer in Layers ?? [] )
		{
			if ( layer is null || !layer.Collides || layer.TileSet is null ) continue;

			foreach ( var area in Merge( layer ) )
			{
				var min = new Vector3( area.Left * size.x, area.Top * size.y, -Thickness * 0.5f );
				var max = new Vector3( ( area.Left + area.Width ) * size.x, ( area.Top + area.Height ) * size.y, Thickness * 0.5f );

				var box = BBox.FromPositionAndSize( ( min + max ) * 0.5f * scale + local.Position, ( max - min ) * scale );

				yield return targetBody.AddBoxShape( box, local.Rotation );
			}
		}
	}

	/// <summary>A layer's solid cells as the fewest rectangles that cover them. Greedy, in one walk bottom to top: from each cell not yet covered, run right while the cells stay solid, then up while the whole run does.</summary>
	public static List<RectInt> Merge( TileMapLayer layer )
	{
		var boxes = new List<RectInt>();
		if ( layer?.TileSet is not { } set ) return boxes;

		var solid = new HashSet<Vector2Int>();

		foreach ( var (cell, value) in layer.Grid.Filled() )
		{
			if ( set.IsSolid( value.Tile ) ) solid.Add( cell );
		}

		if ( solid.Count == 0 ) return boxes;

		//Bottom to top, left to right, so the boxes come out the same every time and every cell is met before the ones above and to the right of it.
		var order = solid.ToList();
		order.Sort( ( a, b ) => a.y != b.y ? a.y.CompareTo( b.y ) : a.x.CompareTo( b.x ) );

		var covered = new HashSet<Vector2Int>();

		bool Free( int x, int y )
		{
			var at = new Vector2Int( x, y );
			return solid.Contains( at ) && !covered.Contains( at );
		}

		foreach ( var start in order )
		{
			if ( covered.Contains( start ) ) continue;

			var width = 1;
			while ( Free( start.x + width, start.y ) ) width++;

			var height = 1;

			while ( true )
			{
				var y = start.y + height;
				var whole = true;

				for ( var x = start.x; x < start.x + width && whole; x++ )
				{
					whole = Free( x, y );
				}

				if ( !whole ) break;

				height++;
			}

			for ( var y = start.y; y < start.y + height; y++ )
			{
				for ( var x = start.x; x < start.x + width; x++ )
				{
					covered.Add( new Vector2Int( x, y ) );
				}
			}

			boxes.Add( new RectInt( start.x, start.y, width, height ) );
		}

		return boxes;
	}
}
