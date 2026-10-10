using System.Text;

namespace Rinten.Tiles;

/// <summary>Which ways a tile's picture is turned over as it is drawn.</summary>
[Flags]
public enum TileFlip : byte
{
	None = 0,
	X = 1,
	Y = 2,
}

//Eight bytes and no meaning of its own - what the number is, is the tile set's business. So a layer can be pointed at another set with the same grid - a winter version of a level - without the level being touched.
/// <summary>One cell of a tile grid: which tile is in it and how that tile is turned.</summary>
public readonly struct TileCell : IEquatable<TileCell>
{
	/// <summary>Which tile, counted from one. Nought is an empty cell.</summary>
	public readonly int Tile;

	/// <summary>Quarter turns anticlockwise, nought to three.</summary>
	public readonly byte Turns;

	public readonly TileFlip Flip;

	public TileCell( int tile, byte turns = 0, TileFlip flip = TileFlip.None )
	{
		Tile = tile;
		Turns = (byte)( turns & 3 );
		Flip = flip;
	}

	public bool IsEmpty => Tile <= 0;

	public static readonly TileCell Empty = new( 0 );

	/// <summary>The turns and the flip as one byte, which is how they are stored.</summary>
	public byte Packed => (byte)( Turns | ( (int)Flip << 2 ) );

	public static TileCell FromPacked( int tile, byte packed ) => new( tile, (byte)( packed & 3 ), (TileFlip)( ( packed >> 2 ) & 3 ) );

	public bool Equals( TileCell other ) => Tile == other.Tile && Turns == other.Turns && Flip == other.Flip;

	public override bool Equals( object obj ) => obj is TileCell other && Equals( other );

	public override int GetHashCode() => HashCode.Combine( Tile, Turns, Flip );

	public static bool operator ==( TileCell a, TileCell b ) => a.Equals( b );

	public static bool operator !=( TileCell a, TileCell b ) => !a.Equals( b );

	public override string ToString() => IsEmpty ? "empty" : $"{Tile} t{Turns} {Flip}";
}

/// <summary>Where the tiles are: a sparse grid of chunks, unbounded in every direction.</summary>
public sealed class TileGrid
{
	/// <summary>How many cells make one chunk along each side.</summary>
	public const int ChunkSize = 32;

	private const int CellsPerChunk = ChunkSize * ChunkSize;

	/// <summary>One square of cells.</summary>
	public sealed class Chunk
	{
		public Vector2Int Coord { get; init; }

		public TileCell[] Cells { get; } = new TileCell[CellsPerChunk];

		/// <summary>How many of its cells hold something. A chunk at nought is dropped.</summary>
		public int Filled { get; internal set; }
	}

	private readonly Dictionary<Vector2Int, Chunk> chunks = new();

	/// <summary>Bumped every time anything changes.</summary>
	public int Revision { get; private set; }

	/// <summary>Every chunk that holds something.</summary>
	public IEnumerable<Chunk> Chunks => chunks.Values;

	public int ChunkCount => chunks.Count;

	/// <summary>Raised with a chunk's coordinate when a cell in it changes.</summary>
	public event Action<Vector2Int> ChunkChanged;

	/// <summary>Which chunk a cell is in.</summary>
	public static Vector2Int ChunkOf( Vector2Int cell ) => new( FloorDiv( cell.x ), FloorDiv( cell.y ) );

	/// <summary>The bottom left cell of a chunk.</summary>
	public static Vector2Int OriginOf( Vector2Int chunk ) => new( chunk.x * ChunkSize, chunk.y * ChunkSize );

	/// <summary>What is in a cell.</summary>
	public TileCell Get( Vector2Int cell )
		=> chunks.TryGetValue( ChunkOf( cell ), out var chunk ) ? chunk.Cells[IndexIn( cell )] : TileCell.Empty;

	/// <summary>Put something in a cell. Returns whether anything changed.</summary>
	public bool Set( Vector2Int cell, TileCell value )
	{
		var coord = ChunkOf( cell );
		var chunk = ChunkAt( coord, !value.IsEmpty );

		if ( chunk is null ) return false;

		var index = IndexIn( cell );
		var was = chunk.Cells[index];

		if ( was == value ) return false;

		chunk.Cells[index] = value;
		chunk.Filled += ( value.IsEmpty ? 0 : 1 ) - ( was.IsEmpty ? 0 : 1 );

		Revision++;
		boundsKnown = false;

		if ( chunk.Filled <= 0 ) chunks.Remove( coord );

		ChunkChanged?.Invoke( coord );

		return true;
	}

	/// <summary>Throw everything away.</summary>
	public void Clear()
	{
		if ( chunks.Count == 0 ) return;

		var coords = chunks.Keys.ToArray();
		chunks.Clear();

		Revision++;
		boundsKnown = false;

		foreach ( var coord in coords ) ChunkChanged?.Invoke( coord );
	}

	/// <summary>Every cell that holds something, with what it holds.</summary>
	public IEnumerable<(Vector2Int Cell, TileCell Value)> Filled()
	{
		foreach ( var chunk in chunks.Values )
		{
			var origin = OriginOf( chunk.Coord );

			for ( var i = 0; i < CellsPerChunk; i++ )
			{
				var value = chunk.Cells[i];
				if ( value.IsEmpty ) continue;

				yield return (new Vector2Int( origin.x + ( i % ChunkSize ), origin.y + ( i / ChunkSize ) ), value);
			}
		}
	}

	/// <summary>The smallest rectangle of cells that holds everything, or an empty one.</summary>
	public RectInt Bounds
	{
		get
		{
			if ( boundsKnown ) return bounds;

			var first = true;
			int minX = 0, minY = 0, maxX = 0, maxY = 0;

			foreach ( var (cell, _) in Filled() )
			{
				if ( first )
				{
					minX = maxX = cell.x;
					minY = maxY = cell.y;
					first = false;
					continue;
				}

				minX = Math.Min( minX, cell.x );
				minY = Math.Min( minY, cell.y );
				maxX = Math.Max( maxX, cell.x );
				maxY = Math.Max( maxY, cell.y );
			}

			bounds = first ? default : new RectInt( minX, minY, maxX - minX + 1, maxY - minY + 1 );
			boundsKnown = true;

			return bounds;
		}
	}

	private RectInt bounds;
	private bool boundsKnown;

	/// <summary>The chunk at this coordinate, made if it is not there and asked for.</summary>
	public Chunk ChunkAt( Vector2Int coord, bool create )
	{
		if ( chunks.TryGetValue( coord, out var chunk ) ) return chunk;
		if ( !create ) return null;

		chunk = new Chunk { Coord = coord };
		chunks[coord] = chunk;

		return chunk;
	}

	/// <summary>Where a cell sits in its chunk's array.</summary>
	public static int IndexIn( Vector2Int cell ) => ( Mod( cell.y ) * ChunkSize ) + Mod( cell.x );

	private static int FloorDiv( int value ) => value >= 0 ? value / ChunkSize : ( ( value + 1 ) / ChunkSize ) - 1;

	private static int Mod( int value )
	{
		var m = value % ChunkSize;
		return m < 0 ? m + ChunkSize : m;
	}

	//Kept in a scene as one string

	private const string Magic = "RTG1";

	/// <summary>The whole grid as one string, which is how a layer keeps it in a scene: chunk by chunk, and inside a chunk a run at a time. A level is mostly runs of one tile and of nothing, so a screen of ground is a few dozen bytes, and it is one property rather than ten thousand.</summary>
	public string Serialize()
	{
		if ( chunks.Count == 0 ) return "";

		var bytes = new List<byte>( 1024 );

		bytes.AddRange( Encoding.ASCII.GetBytes( Magic ) );
		Write( bytes, chunks.Count );

		foreach ( var chunk in chunks.Values )
		{
			Write( bytes, chunk.Coord.x );
			Write( bytes, chunk.Coord.y );

			var index = 0;

			while ( index < CellsPerChunk )
			{
				var value = chunk.Cells[index];
				var run = 1;

				while ( index + run < CellsPerChunk && run < 256 && chunk.Cells[index + run] == value ) run++;

				bytes.Add( (byte)( run - 1 ) );
				Write( bytes, value.Tile );
				bytes.Add( value.Packed );

				index += run;
			}
		}

		return Convert.ToBase64String( bytes.ToArray() );
	}

	/// <summary>Read back what <see cref="Serialize"/> wrote, in place of what is here. Anything else is ignored.</summary>
	public void Deserialize( string text )
	{
		var coordsBefore = chunks.Keys.ToArray();
		chunks.Clear();

		try
		{
			Read( text );
		}
		finally
		{
			Revision++;
			boundsKnown = false;

			foreach ( var coord in coordsBefore ) ChunkChanged?.Invoke( coord );
			foreach ( var coord in chunks.Keys.ToArray() ) ChunkChanged?.Invoke( coord );
		}
	}

	private void Read( string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) ) return;

		byte[] bytes;

		try
		{
			bytes = Convert.FromBase64String( text );
		}
		catch ( FormatException )
		{
			Log.Warning( "Tile grid: not base64, ignored" );
			return;
		}

		if ( bytes.Length < 8 || Encoding.ASCII.GetString( bytes, 0, 4 ) != Magic )
		{
			Log.Warning( "Tile grid: not a tile grid, ignored" );
			return;
		}

		var at = 4;
		var count = Read( bytes, ref at );

		for ( var c = 0; c < count && at + 8 <= bytes.Length; c++ )
		{
			var coord = new Vector2Int( Read( bytes, ref at ), Read( bytes, ref at ) );
			var chunk = ChunkAt( coord, true );

			var index = 0;

			while ( index < CellsPerChunk && at + 6 <= bytes.Length )
			{
				var run = bytes[at++] + 1;
				var tile = Read( bytes, ref at );
				var value = TileCell.FromPacked( tile, bytes[at++] );

				for ( var i = 0; i < run && index < CellsPerChunk; i++, index++ )
				{
					chunk.Cells[index] = value;
					if ( !value.IsEmpty ) chunk.Filled++;
				}
			}

			if ( chunk.Filled <= 0 ) chunks.Remove( coord );
		}
	}

	private static void Write( List<byte> bytes, int value )
	{
		bytes.Add( (byte)( value & 0xFF ) );
		bytes.Add( (byte)( ( value >> 8 ) & 0xFF ) );
		bytes.Add( (byte)( ( value >> 16 ) & 0xFF ) );
		bytes.Add( (byte)( ( value >> 24 ) & 0xFF ) );
	}

	private static int Read( byte[] bytes, ref int at )
	{
		var value = bytes[at] | ( bytes[at + 1] << 8 ) | ( bytes[at + 2] << 16 ) | ( bytes[at + 3] << 24 );
		at += 4;

		return value;
	}
}
