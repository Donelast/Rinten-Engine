using System.Text.Json.Serialization;

namespace Rinten.Tiles;

/// <summary>How a tile stops things that move.</summary>
public enum TileCollision
{
	/// <summary>Nothing: walked through.</summary>
	None,

	/// <summary>A wall from every side.</summary>
	Solid,

	/// <summary>Stood on from above and passed through from below and from the sides - a plank, a cloud. Only <see cref="TileMovement"/> knows it; the physics shapes a map makes are its solid tiles alone.</summary>
	OneWay,
}

/// <summary>What a tile set says about one of its tiles, where it says anything.</summary>
public sealed class TileInfo
{
	/// <summary>Which tile: its cell on the sheet, counted from one, row by row.</summary>
	[Property]
	public int Tile { get; set; } = 1;

	/// <summary>What to call it - how game code asks for a tile by name.</summary>
	[Property]
	public string Name { get; set; } = "";

	[Property]
	public TileCollision Collision { get; set; } = TileCollision.None;

	/// <summary>What it is, in words game code can ask about: "water", "ladder", "hurts".</summary>
	[Property]
	public TagSet Tags { get; set; } = new();

	/// <summary>How many frames it steps through, one after another along the sheet's row from its own cell. One is a still tile. Drawn by the card, by the scene's clock: an animated tile costs nothing a frame.</summary>
	[Property, Range( 1, 64 )]
	public int Frames { get; set; } = 1;

	[Property, Range( 0.1f, 60.0f )]
	public float FramesPerSecond { get; set; } = 8.0f;

	/// <summary>Multiplied into its picture wherever it is drawn.</summary>
	[Property]
	public Color Tint { get; set; } = Color.White;
}

/// <summary>Tiles of one material and which to use per neighbour layout; TilePainter runs it in editor and game</summary>
public sealed class TileTerrain
{
	[Property]
	public string Name { get; set; } = "Terrain";

	/// <summary>Sixteen tiles, one for each way the four sides can be joined: the index is a mask of one for a neighbour above, two right, four below, eight left. Nought is a lone tile, fifteen one with neighbours all round.</summary>
	[Property, Title( "By Neighbours" )]
	public List<int> ByMask { get; set; } = new();

	/// <summary>More tiles that count as this terrain when neighbours are looked at, beyond the sixteen.</summary>
	[Property]
	public List<int> Members { get; set; } = new();

	/// <summary>Whether a tile belongs to this terrain.</summary>
	public bool Contains( int tile )
	{
		if ( tile <= 0 ) return false;

		return ( ByMask is not null && ByMask.Contains( tile ) ) || ( Members is not null && Members.Contains( tile ) );
	}

	/// <summary>Which tile to put down for this set of neighbours, or nought where the table has none.</summary>
	public int Tile( int mask )
	{
		//A table not filled that far has no tile for it - not the last one it has, which put a wrong piece down where the terrain was simply unfinished.
		if ( ByMask is null || mask < 0 || mask >= ByMask.Count ) return 0;

		return ByMask[mask];
	}
}

//Tile number is its sheet cell from one, row by row; zero is empty. Only described tiles are stored
/// <summary>A sheet of tiles: the picture, the grid it is cut on, and what the tiles on it mean. A <see cref="TileMap"/> stores tile numbers; this is what they are.</summary>
[AssetType( Name = "Tile Set", Extension = "tileset", Category = "2D", Icon = "grid_view" )]
public sealed class TileSet : GameResource
{
	/// <summary>The picture the tiles are cut out of.</summary>
	[Property]
	public Texture Sheet
	{
		get;
		set
		{
			if ( ReferenceEquals( field, value ) ) return;

			field = value;
			Changed();
		}
	}

	/// <summary>How big one tile is on the sheet, in pixels.</summary>
	[Property]
	public Vector2Int TileSize
	{
		get;
		set
		{
			field = new Vector2Int( Math.Max( value.x, 1 ), Math.Max( value.y, 1 ) );
			Changed();
		}
	} = new( 16, 16 );

	/// <summary>Pixels of border round the whole sheet before the first tile.</summary>
	[Property]
	public Vector2Int Margin
	{
		get;
		set
		{
			field = new Vector2Int( Math.Max( value.x, 0 ), Math.Max( value.y, 0 ) );
			Changed();
		}
	}

	/// <summary>Pixels between one tile and the next.</summary>
	[Property]
	public Vector2Int Spacing
	{
		get;
		set
		{
			field = new Vector2Int( Math.Max( value.x, 0 ), Math.Max( value.y, 0 ) );
			Changed();
		}
	}

	/// <summary>What is said about particular tiles. A tile that is not here is a plain picture.</summary>
	[Property, WideMode]
	public List<TileInfo> Tiles { get; set; } = new();

	/// <summary>The terrains, for tiles that fit themselves to their neighbours.</summary>
	[Property, WideMode]
	public List<TileTerrain> Terrains { get; set; } = new();

	/// <summary>Bumped whenever anything that changes how tiles are drawn or collide changes.</summary>
	[JsonIgnore, Hide]
	public int Revision { get; private set; }

	/// <summary>Say that something about the tiles changed, so maps drawing them build again.</summary>
	public void Changed()
	{
		Revision++;
		infos = null;
		sprites = null;
	}

	protected override void PostLoad() => Changed();

	protected override void PostReload() => Changed();

	/// <summary>How many tiles a row of the sheet holds.</summary>
	[JsonIgnore, Hide]
	public int Columns => Sheet is null ? 0 : Math.Max( ( Sheet.Width - Margin.x + Spacing.x ) / ( TileSize.x + Spacing.x ), 0 );

	/// <summary>How many rows of tiles the sheet holds.</summary>
	[JsonIgnore, Hide]
	public int Rows => Sheet is null ? 0 : Math.Max( ( Sheet.Height - Margin.y + Spacing.y ) / ( TileSize.y + Spacing.y ), 0 );

	/// <summary>How many tiles the sheet holds.</summary>
	[JsonIgnore, Hide]
	public int Count => Columns * Rows;

	/// <summary>Whether a number is one of this sheet's tiles.</summary>
	public bool Has( int tile ) => tile >= 1 && tile <= Count;

	/// <summary>Where a tile is on the sheet, in pixels from the sheet's top left.</summary>
	public RectInt Region( int tile )
	{
		var columns = Math.Max( Columns, 1 );
		var index = Math.Max( tile - 1, 0 );

		var x = Margin.x + ( index % columns ) * ( TileSize.x + Spacing.x );
		var y = Margin.y + ( index / columns ) * ( TileSize.y + Spacing.y );

		return new RectInt( x, y, TileSize.x, TileSize.y );
	}

	/// <summary>The tile in a cell of the sheet, by its column and row.</summary>
	public int TileAt( int column, int row ) => column < 0 || row < 0 || column >= Columns || row >= Rows ? 0 : row * Columns + column + 1;

	/// <summary>What is said about a tile, or null where nothing is.</summary>
	public TileInfo Info( int tile )
	{
		if ( tile <= 0 ) return null;

		if ( infos is null )
		{
			infos = new Dictionary<int, TileInfo>();

			if ( Tiles is not null )
			{
				foreach ( var info in Tiles )
				{
					if ( info is not null ) infos[info.Tile] = info;
				}
			}
		}

		return infos.TryGetValue( tile, out var found ) ? found : null;
	}

	private Dictionary<int, TileInfo> infos;

	/// <summary>What is said about a tile, made and kept if nothing was yet - for an editor setting it.</summary>
	public TileInfo InfoOrNew( int tile )
	{
		if ( Info( tile ) is { } found ) return found;

		var made = new TileInfo { Tile = tile };
		( Tiles ??= new() ).Add( made );
		Changed();

		return made;
	}

	public TileCollision CollisionOf( int tile ) => Info( tile )?.Collision ?? TileCollision.None;

	public bool IsSolid( int tile ) => CollisionOf( tile ) == TileCollision.Solid;

	public bool HasTag( int tile, string tag ) => Info( tile )?.Tags?.Has( tag ) ?? false;

	/// <summary>The number of the tile with this name, or nought.</summary>
	public int Find( string name )
	{
		if ( string.IsNullOrEmpty( name ) || Tiles is null ) return 0;

		foreach ( var info in Tiles )
		{
			if ( info is not null && string.Equals( info.Name, name, StringComparison.OrdinalIgnoreCase ) ) return info.Tile;
		}

		return 0;
	}

	/// <summary>The tile with this name, as a reference - an invalid one where there is none.</summary>
	public TileReference Get( string name ) => new( this, Find( name ) );

	/// <summary>A tile of this set, as a reference.</summary>
	public TileReference Get( int tile ) => new( this, tile );

	/// <summary>A tile as a sprite: its region of the sheet, and the frames after it along the row where the tile is animated. Made once a tile and kept until the set changes, so everything drawing the same tile shares one.</summary>
	public Sprite SpriteOf( int tile )
	{
		if ( Sheet is null || !Has( tile ) ) return null;

		sprites ??= new();
		if ( sprites.TryGetValue( tile, out var made ) ) return made;

		var info = Info( tile );
		var frames = Math.Max( info?.Frames ?? 1, 1 );
		var animation = new Sprite.Animation { FrameRate = info?.FramesPerSecond ?? 8.0f };

		for ( var i = 0; i < frames; i++ )
		{
			var frame = Has( tile + i ) ? tile + i : tile;
			animation.Frames.Add( new Sprite.Frame { Texture = Sheet, Region = Region( frame ) } );
		}

		made = new Sprite { Animations = [animation] };
		sprites[tile] = made;

		return made;
	}

	private Dictionary<int, Sprite> sprites;

	/// <summary>The terrain with this name, or null.</summary>
	public TileTerrain Terrain( string name )
		=> Terrains?.FirstOrDefault( t => t is not null && string.Equals( t.Name, name, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>The terrain a tile belongs to, or null.</summary>
	public TileTerrain TerrainOf( int tile ) => Terrains?.FirstOrDefault( t => t is not null && t.Contains( tile ) );

	/// <summary>What tile maps are drawn with: a sprite shader, because a tile is drawn the way a sprite is - see TileMap.Rendering. The picture is each tile's own record's, so one material serves every set.</summary>
	public const string TileShader = "shaders/sprite/tile_ps.shader";
}
