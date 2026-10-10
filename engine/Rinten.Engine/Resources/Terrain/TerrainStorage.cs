using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rinten;

//Read off the layer's material - a .mat whose shader declares what a layer knows about itself, shaders/terrain_layer.shader being the engine's own - and written into the numbers picture the ground is drawn by. See Terrain.Params.
/// <summary>What a painted layer switched on for itself.</summary>
[Flags]
public enum TerrainFlags : uint
{
	None = 0,
	NoTile = 1 << 0
}

/// <summary>Compact terrain material encoding with base/overlay texture blending. Packed format (32-bit uint)</summary>
public record struct CompactTerrainMaterial
{
	//Helper struct for cleaner bit field manipulation
	private readonly record struct BitField( int shift, int bits )
	{
		private readonly uint _mask = (1u << bits) - 1;

		/// <summary>Extract value from packed data</summary>
		public uint Extract( uint packed ) => (packed >> shift) & _mask;

		/// <summary>Insert value into packed data</summary>
		public uint Insert( uint packed, uint value )
		{
			packed &= ~(_mask << shift);
			packed |= (value & _mask) << shift;
			return packed;
		}
	}

	private uint _packed;

	//Bit offset definitions
	private static readonly BitField BaseTexture = new( shift: 0, bits: 5 );
	private static readonly BitField OverlayTexture = new( shift: 5, bits: 5 );
	private static readonly BitField Blend = new( shift: 10, bits: 8 );
	private static readonly BitField Hole = new( shift: 18, bits: 1 );
	private static readonly BitField Reserved = new( shift: 19, bits: 13 );

	public CompactTerrainMaterial( uint packed )
	{
		_packed = packed;
	}

	public CompactTerrainMaterial(
		byte baseTextureId,
		byte overlayTextureId = 0,
		byte blendFactor = 0,
		bool isHole = false )
	{
		_packed = 0;
		BaseTextureId = baseTextureId;
		OverlayTextureId = overlayTextureId;
		BlendFactor = blendFactor;
		IsHole = isHole;
		ReservedValue = 0;
	}

	/// <summary>Base texture ID (0-31)</summary>
	public byte BaseTextureId
	{
		get => (byte)BaseTexture.Extract( _packed );
		set => _packed = BaseTexture.Insert( _packed, value );
	}

	/// <summary>Overlay texture ID (0-31)</summary>
	public byte OverlayTextureId
	{
		get => (byte)OverlayTexture.Extract( _packed );
		set => _packed = OverlayTexture.Insert( _packed, value );
	}

	/// <summary>Blend factor between base and overlay (0-255).</summary>
	public byte BlendFactor
	{
		get => (byte)Blend.Extract( _packed );
		set => _packed = Blend.Insert( _packed, value );
	}

	/// <summary>Whether this pixel is marked as a hole</summary>
	public bool IsHole
	{
		get => Hole.Extract( _packed ) != 0;
		set => _packed = Hole.Insert( _packed, value ? 1u : 0u );
	}

	//Reserved bits for future use (13 bits)
	internal ushort ReservedValue
	{
		get => (ushort)Reserved.Extract( _packed );
		set => _packed = Reserved.Insert( _packed, value );
	}

	/// <summary>Raw packed value</summary>
	public uint Packed => _packed;
}

//The mesh is the terrain; the old maps are only used to build a mesh and aren't written after
/// <summary>A terrain as it is kept: the mesh it is, what it is painted with, and the materials of whatever was set into it.</summary>
[Expose]
[AssetType( Name = "Terrain", Extension = "terrain", Category = "World", Icon = "terrain" )]
public partial class TerrainStorage : GameResource
{
	[JsonInclude, JsonPropertyName( "Maps" )] private TerrainMapBlob Maps { get; set; } = new();

	[JsonIgnore, Hide]
	public ushort[] HeightMap
	{
		get => Maps.HeightMap;
		set => Maps.HeightMap = value;
	}

	[JsonIgnore, Hide]
	public UInt32[] ControlMap
	{
		get => Maps.SplatMap;
		set => Maps.SplatMap = value;
	}

	/// <summary>The ground itself. Made from the maps the first time it is asked for, in a terrain that has none of its own yet.</summary>
	[JsonIgnore, Hide]
	public TerrainMesh Mesh
	{
		get
		{
			if ( Maps.Mesh is null ) BuildMeshFromMaps();
			return Maps.Mesh;
		}
		set => Maps.Mesh = value;
	}

	/// <summary>What the geometry set into the ground wears, by the number its triangles carry less one - nought is the terrain's own paint. See TerrainMesh.MaterialOf.</summary>
	public List<Material> InsetMaterials { get; set; } = new();

	/// <summary>How long an edge of the ground is kept under a brush, in metres: longer ones are split and shorter ones folded as the ground is pulled about, so a cave's walls are as finely made as the hill it was dug into.</summary>
	public float EdgeLength { get; set; } = 1.0f;

	/// <summary>Makes the mesh again out of the height map and the control map - a new terrain, an imported height map, a file from before there was a mesh. Whatever was sculpted since is gone.</summary>
	public void BuildMeshFromMaps()
	{
		if ( Resolution < 2 || HeightMap is null || HeightMap.Length < Resolution * Resolution
			|| ControlMap is null || ControlMap.Length < Resolution * Resolution )
		{
			SetMaps( Resolution >= 2 ? Resolution : 512 );
		}

		Maps.Mesh = TerrainMesh.FromHeights( HeightMap, ControlMap, Resolution, TerrainSize, TerrainHeight );
		EdgeLength = MathF.Max( TerrainSize / Math.Max( 1, Resolution ), 0.05f );
	}

	[JsonInclude]
	public int Resolution { get; private set; }

	/// <summary>Uniform world size of the width and length of the terrain.</summary>
	public float TerrainSize { get; set; }

	/// <summary>World size of the maximum height of the terrain.</summary>
	public float TerrainHeight { get; set; }

	//Layers are ordinary materials whose shader declares the layer schema (shaders/terrain_layer.shader)
	/// <summary>What the ground is painted with, in the order the control map indexes them.</summary>
	public List<Material> Materials { get; set; } = new();

	public class TerrainMaterialSettings
	{
		public event Action OnChanged;

		//Titled short: the group already says what they are, and the full names did not fit the inspector's label column.
		[Group( "Height Blend" ), Property, Title( "Enabled" )]
		public bool HeightBlendEnabled
		{
			get => field;
			set
			{
				if ( field == value ) return;
				field = value;
				OnChanged?.Invoke();
			}
		} = true;

		[Group( "Height Blend" ), Property, Range( 0, 1 ), Title( "Sharpness" )]
		public float HeightBlendSharpness
		{
			get => field;
			set
			{
				if ( field == value ) return;
				field = value;
				OnChanged?.Invoke();
			}
		} = 0.87f;

		/// <summary>Texture sampling state used for rendering the terrain.</summary>
		[Property, Advanced]
		public Rendering.SamplerState Sampler
		{
			get => field;
			set
			{
				if ( field == value ) return;
				field = value;
				OnChanged?.Invoke();
			}
		} = new() { Filter = Rendering.FilterMode.Anisotropic, MaxAnisotropy = 8 };
	}

	public TerrainMaterialSettings MaterialSettings { get; set; } = new();

	public TerrainStorage()
	{
		//Half a kilometre across and a quarter of one tall, in metres. These were twenty and
		//ten thousand, which is that same terrain in inches - read as metres it was a continent.
		SetResolution( 512 );
		TerrainSize = 500;
		TerrainHeight = 250;
	}

	protected override void PostLoad()
	{
		//A terrain with a mesh has no use for maps; one without is made from them the first time the mesh is asked for.
		if ( Maps.Mesh is not null ) return;

		if ( Maps.HeightMap.Length == 0 || Maps.SplatMap.Length == 0 )
		{
			SetMaps( Resolution > 0 ? Resolution : 512 );
		}
	}

	/// <summary>A flat terrain of so many cells a side, in place of whatever there was.</summary>
	public void SetResolution( int resolution )
	{
		SetMaps( resolution );
		Maps.Mesh = null;
	}

	private void SetMaps( int resolution )
	{
		Resolution = resolution;

		HeightMap = new ushort[Resolution * Resolution];
		ControlMap = new UInt32[Resolution * Resolution];

		//Initialize compact control map with material 0
		var compactMaterial = new CompactTerrainMaterial( 0 );
		ControlMap.AsSpan().Fill( compactMaterial.Packed );
	}

	//Stores heightmaps and control maps. Serializes as a binary blob for efficiency,
	//with fallback reading of the legacy base64+deflate JSON format for existing files.
	private class TerrainMapBlob : BlobData
	{
		public override int Version => 2;

		public ushort[] HeightMap { get; set; } = Array.Empty<ushort>();
		public uint[] SplatMap { get; set; } = Array.Empty<uint>();

		/// <summary>The ground, once there is one; the maps above are then only what it was made from.</summary>
		public TerrainMesh Mesh;

		//BlobData — binary serialization into the _d sidecar / scene_d file
		public override void Serialize( ref Writer writer )
		{
			//The mesh where there is one, and nothing else: the maps it was made from are not the
			//terrain any more, and a terrain sculpted into caves cannot be put back into them.
			if ( Mesh is not null )
			{
				using var packed = new MemoryStream();
				using ( var into = new BinaryWriter( packed, System.Text.Encoding.UTF8, leaveOpen: true ) ) Mesh.Write( into );

				var meshBytes = Compress<byte>( packed.ToArray() );

				writer.Stream.Write( 1 ); //map_count
				writer.Stream.Write( "mesh" );
				writer.Stream.Write( (byte)1 );
				writer.Stream.Write( (int)packed.Length );
				writer.Stream.Write( meshBytes.Length );
				writer.Stream.Write( meshBytes );
				return;
			}

			writer.Stream.Write( 2 ); //map_count

			//heightmap
			var heightmapBytes = Compress<ushort>( HeightMap );
			writer.Stream.Write( "heightmap" );
			writer.Stream.Write( (byte)sizeof( ushort ) );
			writer.Stream.Write( HeightMap.Length );
			writer.Stream.Write( heightmapBytes.Length );
			writer.Stream.Write( heightmapBytes );

			//splatmap
			var splatmapBytes = Compress<uint>( SplatMap );
			writer.Stream.Write( "splatmap" );
			writer.Stream.Write( (byte)sizeof( uint ) );
			writer.Stream.Write( SplatMap.Length );
			writer.Stream.Write( splatmapBytes.Length );
			writer.Stream.Write( splatmapBytes );
		}

		public override void Deserialize( ref Reader reader )
		{
			int mapCount = reader.Stream.Read<int>();

			for ( int i = 0; i < mapCount; i++ )
			{
				var name = reader.Stream.Read<string>();
				reader.Stream.Read<byte>(); //elementSize (informational)
				reader.Stream.Read<int>(); //elementCount (informational)
				int compressedLength = reader.Stream.Read<int>();
				var compressed = new byte[compressedLength];
				reader.Stream.Read( compressed, 0, compressedLength );

				switch ( name )
				{
					case "heightmap":
						HeightMap = Decompress<ushort>( compressed ).ToArray();
						break;
					case "splatmap":
						SplatMap = Decompress<uint>( compressed ).ToArray();
						break;
					case "mesh":
						using ( var from = new BinaryReader( new MemoryStream( Decompress<byte>( compressed ).ToArray() ) ) )
							Mesh = TerrainMesh.Read( from );
						break;
						//Unknown map type: skip (forward compatibility)
				}
			}
		}

		//Compression helpers — also used by JsonUpgrader methods in TerrainStorage.Upgrade.cs
		internal static Span<T> Decompress<T>( byte[] compressedData ) where T : unmanaged
		{
			using var compressedStream = new MemoryStream( compressedData );
			using var decompressedStream = new MemoryStream();
			using ( var deflateStream = new DeflateStream( compressedStream, CompressionMode.Decompress ) )
			{
				deflateStream.CopyTo( decompressedStream );
			}

			return MemoryMarshal.Cast<byte, T>( decompressedStream.ToArray().AsSpan() );
		}

		internal static byte[] Compress<T>( Span<T> data ) where T : struct
		{
			using var memoryStream = new MemoryStream();
			using ( var deflateStream = new DeflateStream( memoryStream, CompressionMode.Compress ) )
			{
				deflateStream.Write( MemoryMarshal.AsBytes( data ) );
			}

			return memoryStream.ToArray();
		}
	}
}
