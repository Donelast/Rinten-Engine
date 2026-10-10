using System.Text.Json.Serialization;

namespace Rinten.Clutter;

/// <summary>A weighted collection of Prefabs and Models for random selection during clutter placement.</summary>
[AssetType( Name = "Clutter", Extension = "clutter", Category = "World", Icon = "grass" )]
public class ClutterDefinition : GameResource
{
	/// <summary>Streaming tile width in metres</summary>
	public enum TileSizeOption
	{
		[Title( "8 m" )] Size8 = 8,
		[Title( "16 m" )] Size16 = 16,
		[Title( "32 m" )] Size32 = 32,
		[Title( "64 m" )] Size64 = 64,
		[Title( "128 m" )] Size128 = 128,

		//Hidden aliases for old inch-based names so old .clutter files load; visible names come first
		[Hide] Size256 = Size8,
		[Hide] Size512 = Size16,
		[Hide] Size1024 = Size32,
		[Hide] Size2048 = Size64,
		[Hide] Size4096 = Size128
	}

	/// <summary>List of weighted entries</summary>
	[Property]
	[ClutterEntriesGrid]
	public List<ClutterEntry> Entries { get; set; } = [];

	public bool IsEmpty => Entries.Count == 0;

	/// <summary>How big each tile is, in metres, in infinite streaming mode.</summary>
	[Property]
	[Title( "Tile Size" )]
	public TileSizeOption TileSizeEnum { get; set; } = TileSizeOption.Size16;

	/// <summary>Gets the tile size as a float value.</summary>
	[Hide, JsonIgnore]
	public float TileSize => (float)TileSizeEnum;

	/// <summary>Number of tiles to generate around the camera in each direction. Higher values = more visible range but more memory usage.</summary>
	[Property, Range( 1, 10 )]
	public int TileRadius { get; set; } = 4;

#pragma warning disable CS0618 //AnyOfType<T> is obsolete; this property still needs a replacement
	[Property, InlineEditor]
	public AnyOfType<Scatterer> Scatterer { get; set; } = new SimpleScatterer();
#pragma warning restore CS0618

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( TileSize );
		hash.Add( TileRadius );
		hash.Add( Entries.Count );

		foreach ( var entry in Entries )
		{
			if ( entry != null )
			{
				hash.Add( entry.Weight );
				hash.Add( entry.LocalScale );
				hash.Add( entry.CastShadows );
				hash.Add( entry.EnablePhysics );
				hash.Add( entry.Model?.GetHashCode() ?? 0 );
				hash.Add( entry.Prefab?.GetHashCode() ?? 0 );
			}
		}

		hash.Add( Scatterer.Value?.GetHashCode() ?? 0 );
		return hash.ToHashCode();
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "forest", width, height );
	}
}
