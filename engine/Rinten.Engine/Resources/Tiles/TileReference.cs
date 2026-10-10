using System.Text.Json.Serialization;

namespace Rinten.Tiles;

/// <summary>A tile of a tile set by number (its sheet cell), so it survives renames; e.g. tileSet.Get("crate")</summary>
public struct TileReference : IEquatable<TileReference>
{
	/// <summary>The set the tile is from.</summary>
	public TileSet Set { get; set; }

	/// <summary>Which tile: its cell on the sheet, counted from one. Nought is none.</summary>
	public int Tile { get; set; }

	public TileReference( TileSet set, int tile )
	{
		Set = set;
		Tile = tile;
	}

	/// <summary>Whether it names a tile the set has.</summary>
	[JsonIgnore]
	public readonly bool IsValid => Set is not null && Set.Has( Tile );

	/// <summary>What the set says about the tile, or null where it says nothing.</summary>
	[JsonIgnore]
	public readonly TileInfo Info => Set?.Info( Tile );

	/// <summary>What the set calls it, or empty.</summary>
	[JsonIgnore]
	public readonly string Name => Info?.Name ?? "";

	/// <summary>How it stops things that move.</summary>
	[JsonIgnore]
	public readonly TileCollision Collision => Set?.CollisionOf( Tile ) ?? TileCollision.None;

	/// <summary>The tile as a sprite, animated where the tile is - for drawing it as a thing rather than in a map.</summary>
	public readonly Sprite ToSprite() => Set?.SpriteOf( Tile );

	/// <summary>This tile, or where it names none, the set's tile of this name.</summary>
	public readonly int Or( TileSet set, string name ) => IsValid ? Tile : set?.Find( name ) ?? 0;

	public readonly bool Equals( TileReference other ) => ReferenceEquals( Set, other.Set ) && Tile == other.Tile;
	public override readonly bool Equals( object obj ) => obj is TileReference other && Equals( other );
	public override readonly int GetHashCode() => HashCode.Combine( Set, Tile );

	public static bool operator ==( TileReference a, TileReference b ) => a.Equals( b );
	public static bool operator !=( TileReference a, TileReference b ) => !a.Equals( b );

	/// <summary>The number, so a reference can be put straight into a map.</summary>
	public static implicit operator int( TileReference reference ) => reference.Tile;

	public override readonly string ToString() => IsValid ? ( Name.Length > 0 ? $"{Name} ({Tile})" : $"tile {Tile}" ) : "none";
}
