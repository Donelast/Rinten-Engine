namespace Rinten;

/// <summary>One cell of the world's grid: a square of the ground plane (x and z) at a level, from <see cref="WorldGrid.CellSize"/> metres at level nought, each level twice the one below. Its height is whatever stands in it.</summary>
public readonly record struct WorldCell( int X, int Z, int Level )
{
	/// <summary>How wide the cell is, in metres.</summary>
	public float Size => WorldGrid.SizeOf( Level );

	/// <summary>The corner with the least x and z.</summary>
	public Vector2 Min => new( X * Size, Z * Size );

	/// <summary>The cell one level up that holds this one, or itself at the top.</summary>
	public WorldCell Parent => Level >= WorldGrid.Levels - 1 ? this
		: new WorldCell( X >> 1, Z >> 1, Level + 1 );

	/// <summary>The four cells one level down, or none at level nought.</summary>
	public IEnumerable<WorldCell> Children()
	{
		if ( Level == 0 ) yield break;

		for ( var dz = 0; dz < 2; dz++ )
			for ( var dx = 0; dx < 2; dx++ )
				yield return new WorldCell( ( X << 1 ) + dx, ( Z << 1 ) + dz, Level - 1 );
	}

	/// <summary>The cell's box, from <paramref name="low"/> to <paramref name="high"/> on y.</summary>
	public BBox Box( float low, float high )
		=> new( new Vector3( X * Size, low, Z * Size ), new Vector3( ( X + 1 ) * Size, high, ( Z + 1 ) * Size ) );

	/// <summary>One number for the cell: the level, then x and z, 28 bits each.</summary>
	public long Key => ( (long)Level << 56 ) | ( ( (long)X & 0xFFFFFFF ) << 28 ) | ( (long)Z & 0xFFFFFFF );
}

/// <summary>The world's loose spatial grid: 32 m cells at level 0, doubling up to 2 km; objects filed by centre</summary>
public static class WorldGrid
{
	/// <summary>How wide a level-nought cell is, in metres.</summary>
	public const float CellSize = 32.0f;

	/// <summary>How many levels: thirty-two metres to two kilometres.</summary>
	public const int Levels = 7;

	/// <summary>How wide a cell of <paramref name="level"/> is.</summary>
	public static float SizeOf( int level ) => CellSize * ( 1 << Math.Clamp( level, 0, Levels - 1 ) );

	/// <summary>The cell of <paramref name="level"/> a point of the ground plane lies in.</summary>
	public static WorldCell At( Vector3 position, int level = 0 )
	{
		var size = SizeOf( level );
		return new WorldCell( (int)MathF.Floor( position.x / size ), (int)MathF.Floor( position.z / size ), Math.Clamp( level, 0, Levels - 1 ) );
	}

	/// <summary>The level whose cell is at least as wide as something <paramref name="radius"/> metres round.</summary>
	public static int LevelFor( float radius )
	{
		var level = 0;
		while ( level < Levels - 1 && SizeOf( level ) < radius * 2.0f ) level++;
		return level;
	}

	/// <summary>The cell something with this sphere is filed in: its middle, at the level its size wants.</summary>
	public static WorldCell CellFor( Vector3 centre, float radius ) => At( centre, LevelFor( radius ) );

	/// <summary>Every cell of <paramref name="level"/> whose square comes within <paramref name="radius"/> of <paramref name="centre"/> on the ground plane.</summary>
	public static IEnumerable<WorldCell> Around( Vector3 centre, float radius, int level = 0 )
	{
		var size = SizeOf( level );
		var x0 = (int)MathF.Floor( ( centre.x - radius ) / size );
		var x1 = (int)MathF.Floor( ( centre.x + radius ) / size );
		var z0 = (int)MathF.Floor( ( centre.z - radius ) / size );
		var z1 = (int)MathF.Floor( ( centre.z + radius ) / size );

		for ( var z = z0; z <= z1; z++ )
		{
			for ( var x = x0; x <= x1; x++ )
			{
				//The nearest point of the square to the centre, on the plane.
				var nx = Math.Clamp( centre.x, x * size, ( x + 1 ) * size ) - centre.x;
				var nz = Math.Clamp( centre.z, z * size, ( z + 1 ) * size ) - centre.z;

				if ( ( nx * nx ) + ( nz * nz ) <= radius * radius ) yield return new WorldCell( x, z, level );
			}
		}
	}

	/// <summary>Every cell of <paramref name="level"/> within <paramref name="reach"/> of <paramref name="centre"/> whose box, from <paramref name="low"/> to <paramref name="high"/> on y, the frustum sees.</summary>
	public static IEnumerable<WorldCell> InFrustum( Frustum frustum, Vector3 centre, float reach, float low, float high, int level = 0 )
	{
		foreach ( var cell in Around( centre, reach, level ) )
			if ( frustum.IsInside( cell.Box( low, high ), true ) ) yield return cell;
	}
}
