using Rinten.UI;
using System.Text.Json.Serialization;
using System.Threading;

namespace Rinten.Resources;

/// <summary>Load images from disk and convert them to textures</summary>
[Order( -100 )]
[Title( "Image File" )]
[Icon( "image" )]
[ClassName( "imagefile" )]
public class ImageFileGenerator : TextureGenerator
{
	/// <summary>The path to the image file, relative to any other assets in the project.</summary>
	[Header( "Image" )]
	[KeyProperty, TextureImagePath]
	public string FilePath { get; set; }

	/// <summary>The maximum size of the image in pixels. If the imported image is larger than this (after cropping), it will be downscaled to fit.</summary>
	public int MaxSize { get; set; } = 4096;

	/// <summary>When enabled, the output texture will be a normal map generated from the heightmap of the image.</summary>
	[Header( "Normal Map" ), Title( "Height to Normal" )]
	public bool ConvertHeightToNormals { get; set; }

	/// <summary>The scale of the normal map when using <see cref="ConvertHeightToNormals"/>. If negative, the normal map will be inverted.</summary>
	[ShowIf( "ConvertHeightToNormals", true )]
	public float NormalScale { get; set; } = 1;

	/// <summary>How much to rotate the image by, in degrees. This is applied after cropping and padding.</summary>
	[Header( "Adjust" ), Range( 0, 360 )]
	public float Rotate { get; set; } = 0.0f;

	/// <summary>Whether or not to flip the image vertically. This is done after everything else has been applied.</summary>
	public bool FlipVertical { get; set; } = false;

	/// <summary>Whether or not to flip the image horizontally. This is done after everything else has been applied.</summary>
	public bool FlipHorizontal { get; set; } = false;

	/// <summary>How many pixels from each edge to crop from the image. If negative values are used, the image will be expanded instead of cropped.</summary>
	public Margin Cropping { get; set; }

	/// <summary>How many pixels of padding from each edge. After the image has been cropped, padding is added without affecting the size of the image (scaling the original image down to fit padded margins).</summary>
	public Margin Padding { get; set; }

	/// <summary>Whether or not to invert the colors of the image.</summary>
	[Header( "Effects" )]
	public bool InvertColor { get; set; } = false;

	/// <summary>The color the image should be tinted. This effectively multiplies the color of each pixel by this color (including alpha).</summary>
	public Color Tint { get; set; } = Color.White;

	/// <summary>The intensity of the blur effect. If 0, no blur is applied.</summary>
	[Range( 0, 32 )]
	public float Blur { get; set; } = 0.0f;

	/// <summary>The intensity of the sharpen effect. If 0, no sharpening is applied.</summary>
	[Range( 0, 10 )]
	public float Sharpen { get; set; } = 0.0f;

	/// <summary>The brightness of the image.</summary>
	[Range( 0, 2 )]
	public float Brightness { get; set; } = 1.0f;

	/// <summary>The contrast of the image.</summary>
	[Range( 0, 2 )]
	public float Contrast { get; set; } = 1.0f;

	/// <summary>The saturation of the image.</summary>
	[Range( 0, 2 )]
	public float Saturation { get; set; } = 1.0f;

	/// <summary>How much to adjust the hue of the image, in degrees. If 0, no hue adjustment is applied.</summary>
	[Range( 0, 360 )]
	public float Hue { get; set; } = 0.0f;

	/// <summary>When enabled, every pixel in the image will be re-colored to the <see cref="TargetColor"/> (interpolated by the alpha).</summary>
	[ToggleGroup( "Colorize" )]
	public bool Colorize { get; set; }

	/// <summary>When <see cref="Colorize"/> is enabled, this is the target color that every pixel in the image will be re-colored to.</summary>
	[Group( "Colorize" )]
	public Color TargetColor { get; set; } = Color.White;

	[Hide]
	public override bool CacheToDisk => true;

	[Hide]
	public override ImageFormat? FormatOverride => ConvertHeightToNormals ? null : ImageFormat.RGBA8888;

	//Don't continually load from disk, cache that shit
	[JsonIgnore, Hide]
	int _loadedCacheHash;

	[JsonIgnore, Hide]
	Bitmap _cacheLoaded;

	[JsonIgnore, Hide]
	int loadCount = 0;

	//Names already complained about - see LoadCached.
	private static readonly HashSet<string> absent = new( StringComparer.OrdinalIgnoreCase );

	//Which version of the picture on disk this is: its size and when it was written. Nought for a picture that is not there.
	long FileStamp()
	{
		if ( string.IsNullOrWhiteSpace( FilePath ) ) return 0;
		if ( EngineFileSystem.Mounted is not { } mounted ) return 0;

		var path = FilePath.NormalizeFilename();

		try
		{
			if ( !mounted.FileExists( path ) ) return 0;
			return mounted.LastWriteTime( path ).Ticks ^ ( mounted.FileSize( path ) << 40 );
		}
		catch ( System.IO.IOException )
		{
			return 0;
		}
	}

	/// <summary>The settings and the picture they were applied to. The settings alone were the key of the generated texture cache, so a png drawn over kept handing out what it used to look like - to a sprite made again from it as much as to the one that had it - until the editor was restarted.</summary>
	public override ulong GetHash()
	{
		return base.GetHash() * 31 + (ulong)FileStamp();
	}

	//The textures made from a picture, and the version of the picture each was made from - so that drawing over the picture redraws them.
	private sealed record Drawn( WeakReference<Texture> Texture, ImageFileGenerator Generator, string Path )
	{
		public long Stamp { get; set; }
	}

	private static readonly List<Drawn> made = new();

	protected override void Made( Texture output )
	{
		if ( output is null || ReferenceEquals( output, Texture.Invalid ) ) return;
		if ( string.IsNullOrWhiteSpace( FilePath ) ) return;

		//A compiled texture is the one everybody shares - it is not this generator's to redraw, and the resource system reloads it itself.
		if ( FilePath.EndsWith( ".tex", StringComparison.OrdinalIgnoreCase ) ) return;

		//A copy, so that settings changed on this one in an inspector after the fact do not change what the texture is redrawn as.
		var copy = new ImageFileGenerator();
		copy.Deserialize( Json.SerializeAsObject( this ) );

		lock ( made )
		{
			made.RemoveAll( x => !x.Texture.TryGetTarget( out _ ) );
			made.Add( new Drawn( new WeakReference<Texture>( output ), copy, FilePath.NormalizeFilename() ) { Stamp = FileStamp() } );
		}
	}

	//A file changed on disk: every texture made from it, that is still about, is made again
	//from what is there now. Told more than once about the same write, it redraws once.
	internal static void FileChanged( string filename )
	{
		if ( string.IsNullOrWhiteSpace( filename ) ) return;
		if ( !TextureLoader.Image.IsAppropriate( filename ) ) return;

		var path = filename.NormalizeFilename();

		List<(Texture, Drawn)> stale = new();

		lock ( made )
		{
			made.RemoveAll( x => !x.Texture.TryGetTarget( out _ ) );

			foreach ( var m in made )
			{
				if ( !string.Equals( m.Path, path, StringComparison.OrdinalIgnoreCase ) ) continue;
				if ( !m.Texture.TryGetTarget( out var texture ) ) continue;

				var stamp = m.Generator.FileStamp();
				if ( stamp == 0 || stamp == m.Stamp ) continue;

				m.Stamp = stamp;
				stale.Add( (texture, m) );
			}
		}

		foreach ( var (texture, m) in stale )
		{
			_ = Redraw( texture, m.Generator );
		}
	}

	static async Task Redraw( Texture texture, ImageFileGenerator generator )
	{
		var fresh = await Task.Run( async () => await generator.CreateTexture( default, default ) );

		if ( fresh is null || ReferenceEquals( fresh, Texture.Invalid ) || ReferenceEquals( fresh, texture ) )
			return;

		await MainThread.Wait();

		if ( !texture.CopyFrom( fresh ) )
			fresh.Dispose();
	}

	async Task<Bitmap> LoadCached()
	{
		if ( string.IsNullOrWhiteSpace( FilePath ) ) return default;
		var path = FilePath.NormalizeFilename();

		if ( string.IsNullOrEmpty( FilePath ) ) return default;
		if ( !EngineFileSystem.Mounted.FileExists( path ) )
		{
			//Once per name. Each of the engine's own impact decals names four pictures and every one of them is asked for again each
			//time the decal is loaded, so a handful of surfaces came to a hundred lines of a log that said the same nine things.
			lock ( absent )
			{
				if ( absent.Add( path ?? "" ) )
					Log.Warning( $"ImageFileGenerator could not find file: {path}" );
			}

			return default;
		}

		var size = EngineFileSystem.Mounted.FileSize( path );

		var hash = HashCode.Combine( size, FilePath, Cropping, EngineFileSystem.Mounted.LastWriteTime( path ) );
		if ( hash == _loadedCacheHash )
			return _cacheLoaded?.Clone();

		var bytes = await EngineFileSystem.Mounted.ReadAllBytesAsync( path );

		//Create the bitmap and crop it if needed
		var bitmap = Bitmap.CreateFromBytes( bytes );

		//An image nothing here can decode. Said once, with the name, instead of throwing into whatever asked for the texture: this ran
		//three hundred times over one imported project and every one of them arrived as a bare null reference on the finalizer thread.
		if ( bitmap is null )
		{
			Log.Warning( $"ImageFileGenerator could not read an image out of: {path}" );
			return default;
		}

		var newRect = new Rect( Cropping.Left, Cropping.Top, bitmap.Width - Cropping.Right - Cropping.Left, bitmap.Height - Cropping.Bottom - Cropping.Top );
		if ( newRect.Width > 0 && newRect.Height > 0 )
		{
			bitmap = bitmap.Crop( newRect.SnapToGrid() );
		}

		if ( loadCount > 3 )
		{
			_loadedCacheHash = hash;
			_cacheLoaded = bitmap?.Clone();
		}

		loadCount++;

		return bitmap;
	}

	protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		if ( string.IsNullOrWhiteSpace( FilePath ) )
			return null;

		//A regular texture!

		//Trim compiled names!
		if ( FilePath.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			FilePath = FilePath[..^2];

		if ( FilePath.EndsWith( ".tex", StringComparison.OrdinalIgnoreCase ) )
		{
			await MainThread.Wait();
			return Texture.Load( FilePath );
		}

		var bitmap = await LoadCached();
		if ( bitmap is null ) return Texture.Invalid;

		//Tell the compiler we're using this file, so to add it as a compile reference
		if ( options.Compiler is not null )
		{
			options.Compiler.Context.AddCompileReference( FilePath );
		}

		if ( !Padding.IsNearlyZero() )
		{
			bitmap.InsertPadding( Padding );
		}


#pragma warning disable CA2000 //Dispose objects before losing scope
		//Disposal is handled after creating the new bitmap to keep the source alive during processing
		if ( Rotate != 0 )
		{
			var rotated = bitmap.Rotate( Rotate );
			bitmap.Dispose();
			bitmap = rotated;
		}

		if ( bitmap.Width > MaxSize || bitmap.Height > MaxSize )
		{
			float scale = Math.Min( (float)MaxSize / bitmap.Width, (float)MaxSize / bitmap.Height );
			int newWidth = (int)(bitmap.Width * scale);
			int newHeight = (int)(bitmap.Height * scale);

			newWidth = newWidth.Clamp( 1, 1024 * 8 );
			newHeight = newHeight.Clamp( 1, 1024 * 8 );

			var resized = bitmap.Resize( newWidth, newHeight );
			bitmap.Dispose();
			bitmap = resized;
		}

		if ( Tint != Color.White || Tint.a != 1 )
		{
			bitmap.Tint( Tint );
		}
		if ( Sharpen > 0.0f ) bitmap.Sharpen( Sharpen, true );
		if ( Blur > 0.0f ) bitmap.Blur( Blur, true );
		if ( Brightness != 1 || Contrast != 1 || Saturation != 1 || Hue != 0 ) bitmap.Adjust( Brightness, Contrast, Saturation, Hue );
		if ( InvertColor ) bitmap.InvertColor();

		if ( Colorize )
		{
			bitmap.Colorize( TargetColor );
		}

		//if ( Posterize != 0 ) bitmap.Posterize( Posterize );

		if ( FlipHorizontal )
		{
			var flipped = bitmap.FlipHorizontal();
			bitmap.Dispose();
			bitmap = flipped;
		}
		if ( FlipVertical )
		{
			var flipped = bitmap.FlipVertical();
			bitmap.Dispose();
			bitmap = flipped;
		}

		if ( ConvertHeightToNormals )
		{
			var normalMap = bitmap.HeightmapToNormalMap( NormalScale );
			bitmap.Dispose();
			bitmap = normalMap;
		}
#pragma warning restore CA2000 //Dispose objects before losing scope

		var tex = bitmap.ToTexture();
		bitmap?.Dispose();

		return tex;
	}

	public override EmbeddedResource? CreateEmbeddedResource()
	{
		//if we're a vtex, don't create an embedded resource
		if ( FilePath.EndsWith( ".tex", StringComparison.OrdinalIgnoreCase ) )
			return null;

		return base.CreateEmbeddedResource();
	}
}
