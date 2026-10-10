using WidgetControlSheet = global::Editor.ControlSheet;

namespace Editor.Inspectors;

/// <summary>Picture settings kept in .meta and baked into its blocks (see PictureBake)</summary>
[CanEdit( "asset:png" )]
[CanEdit( "asset:jpg" )]
[CanEdit( "asset:jpeg" )]
[CanEdit( "asset:tga" )]
[CanEdit( "asset:bmp" )]
[CanEdit( "asset:gif" )]
[CanEdit( "asset:webp" )]
public class TextureCompileSettings : Widget, IAssetInspector
{
	private Asset Asset;

	public class Settings
	{
		[Title( "No Mipmaps" )]
		[Description( "Keep only the full-size picture - for UI and pixel art that is never drawn smaller" )]
		public bool NoMip { get; set; }

		[Title( "No Compression" )]
		[Description( "Store every pixel as it is instead of in compressed blocks - four to eight times the memory, no block artifacts" )]
		public bool NoCompress { get; set; }

		[Range( 0, 2 )]
		[Description( "Multiplies the colour, leaving the alpha as it is" )]
		public float Brightness { get; set; } = 1.0f;
	}

	readonly Settings settings = new();

	public TextureCompileSettings( Widget parent ) : base( parent )
	{
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset asset )
	{
		Asset = asset;

		settings.NoMip = Asset.MetaData.Get<bool>( "nomip" );
		settings.NoCompress = Asset.MetaData.Get<bool>( "nocompress" );
		settings.Brightness = Asset.MetaData.Get( "brightness", 1.0f );

		var so = settings.GetSerialized();
		Layout = WidgetControlSheet.Create( so );

		so.OnPropertyChanged += OnPropertyChanged;
	}

	public void OnPropertyChanged( SerializedProperty prop )
	{
		//Written and baked once the edit settles: a brightness drag would otherwise compile the picture again on every frame of it.
		_dirty = true;
		_sinceChange = 0;
	}

	private bool _dirty;
	private RealTimeSince _sinceChange;

	[EditorEvent.Frame]
	private void Settle()
	{
		if ( !_dirty || _sinceChange < 0.3f ) return;

		Flush();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		if ( _dirty ) Flush();
	}

	private void Flush()
	{
		_dirty = false;

		if ( Asset?.MetaData is not { } meta ) return;

		meta.Set( "nomip", settings.NoMip );
		meta.Set( "nocompress", settings.NoCompress );
		meta.Set( "brightness", settings.Brightness );

		//Settings nothing reads, from before.
		meta.Set<object>( "clampu", null );
		meta.Set<object>( "clampv", null );
		meta.Set<object>( "clampw", null );
		meta.Set<object>( "nolod", null );

		//The .meta is newer than the blocks now, which is what makes this throw them away to be drawn again with the settings.
		Asset.Compile( false );
	}
}
