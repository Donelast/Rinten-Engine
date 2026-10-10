namespace Rinten.Tiles;

/// <summary>Moves a box through a tile map without physics, one axis at a time, for exact platformer feel</summary>
public static class TileMovement
{
	private const float Skin = 0.0005f;

	/// <summary>Whether a box overlaps anything it cannot be in: a solid tile of any layer that collides.</summary>
	public static bool Overlaps( TileMap map, Vector2 min, Vector2 size )
		=> Blocked( map, min, size, oneWayTop: float.NaN );

	/// <summary>Move a box by this much, stopping it against whatever it runs into. Returns how far it actually went.</summary>
	public static Vector2 Move( TileMap map, ref Vector2 min, Vector2 size, Vector2 delta, out bool hitX, out bool hitY )
	{
		hitX = false;
		hitY = false;

		if ( map is null )
		{
			min += delta;
			return delta;
		}

		var moved = Vector2.Zero;

		//Sideways first, then up and down: a box that walks into a wall keeps falling, and one that lands keeps its sideways speed.
		if ( delta.x != 0.0f )
		{
			var wanted = new Vector2( min.x + delta.x, min.y );

			if ( Blocked( map, wanted, size, float.NaN ) )
			{
				min.x = Edge( map, min, size, delta.x, true, float.NaN );
				hitX = true;
			}
			else
			{
				min.x = wanted.x;
				moved.x = delta.x;
			}
		}

		if ( delta.y != 0.0f )
		{
			var wanted = new Vector2( min.x, min.y + delta.y );

			//Falling: a one-way tile whose top the box was at or above stops it.
			var oneWayTop = delta.y < 0.0f ? min.y : float.NaN;

			if ( Blocked( map, wanted, size, oneWayTop ) )
			{
				min.y = Edge( map, min, size, delta.y, false, oneWayTop );
				hitY = true;
			}
			else
			{
				min.y = wanted.y;
				moved.y = delta.y;
			}
		}

		return moved;
	}

	/// <summary>Whether a box is standing on something - solid or one-way.</summary>
	public static bool OnGround( TileMap map, Vector2 min, Vector2 size, float reach = 0.02f )
		=> Blocked( map, new Vector2( min.x, min.y - reach ), new Vector2( size.x, reach ), oneWayTop: min.y );

	//Whether a box overlaps a tile that stops it. oneWayTop is where the box's bottom was before it moved, when
	//it is coming down: a one-way tile whose top is at or below that stops it. NaN leaves one-way tiles out.
	private static bool Blocked( TileMap map, Vector2 min, Vector2 size, float oneWayTop )
	{
		if ( map?.Layers is null ) return false;

		var step = map.CellSize;
		var max = min + size;

		var fromX = (int)MathF.Floor( min.x / step.x );
		var toX = (int)MathF.Ceiling( max.x / step.x ) - 1;
		var fromY = (int)MathF.Floor( min.y / step.y );
		var toY = (int)MathF.Ceiling( max.y / step.y ) - 1;

		foreach ( var layer in map.Layers )
		{
			if ( layer is null || !layer.Collides || layer.TileSet is null ) continue;

			for ( var y = fromY; y <= toY; y++ )
			{
				for ( var x = fromX; x <= toX; x++ )
				{
					var collision = layer.CollisionAt( new Vector2Int( x, y ) );

					if ( collision == TileCollision.Solid ) return true;

					if ( collision == TileCollision.OneWay && !float.IsNaN( oneWayTop ) && oneWayTop >= ( y + 1 ) * step.y - Skin )
						return true;
				}
			}
		}

		return false;
	}

	//How far along one axis the box can go before it touches something - found by halving the step, so a fast body costs what a slow one does.
	private static float Edge( TileMap map, Vector2 min, Vector2 size, float delta, bool horizontal, float oneWayTop )
	{
		var low = 0.0f;
		var high = delta;

		for ( var i = 0; i < 14; i++ )
		{
			var middle = ( low + high ) * 0.5f;
			var at = horizontal ? new Vector2( min.x + middle, min.y ) : new Vector2( min.x, min.y + middle );

			if ( Blocked( map, at, size, oneWayTop ) ) high = middle;
			else low = middle;
		}

		var reached = low - ( MathF.Sign( delta ) * Skin );

		return horizontal ? min.x + reached : min.y + reached;
	}
}
