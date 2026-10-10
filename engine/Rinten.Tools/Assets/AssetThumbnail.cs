using Native;
using System;
using System.IO;

namespace Editor;

static class AssetThumbnail
{
	const int ImageEdge = 256;
	const int MaterialPreviewVersion = 3;

	//Bumped when the lighting of rendered thumbnails changes, to invalidate cached ones
	const int SceneLightVersion = 1;

	//Bumped when export thumbnails change, so cached ones (once black squares) are redrawn
	const int MeshPreviewVersion = 2;

	//Bumped when what a sound event's thumbnail looks like changes. A sound event was rendered as its empty preview world,
	//which is a black square, and those squares are in the cache. It draws its first sound's waveform now - see PreviewSound.
	const int SoundEventPreviewVersion = 1;

	static readonly Dictionary<string, (long Ticks, Pixmap Pixmap)> ImageThumbs = new( StringComparer.OrdinalIgnoreCase );

	internal static string GetThumbnailFile( Asset asset, bool createDirectory )
	{
		bool isCloud = asset.AbsolutePath.Contains( Project.CloudPath );
		var cacheFolder = $"/{(isCloud ? "thumbnails/.cloud" : "thumbnails")}/{System.IO.Path.GetDirectoryName( asset.Path )}";
		cacheFolder = cacheFolder.Replace( ":", "" );

		var version = asset.Path.EndsWith( ".mat", StringComparison.OrdinalIgnoreCase )
			? $".preview{MaterialPreviewVersion}"
			: asset.AssetType == AssetType.Mesh
				? $".preview{MeshPreviewVersion}"
				: asset.AssetType == AssetType.SoundEvent
					? $".preview{SoundEventPreviewVersion}"
					: $".lit{SceneLightVersion}";
		var cacheName = $"{cacheFolder}/{System.IO.Path.GetFileName( asset.Path )}{version}.png";
		var fullPath = FileSystem.ProjectTemporary.GetFullPath( cacheName );

		if ( createDirectory )
			FileSystem.ProjectTemporary.CreateDirectory( cacheFolder );

		return fullPath;
	}

	//Get an asset thumb. Will generate it if not immediately available, but will wait for it to be generated before returning.
	internal static async Task<Pixmap> GetAssetThumbAsync( Asset asset )
	{
		ArgumentNullException.ThrowIfNull( asset, nameof( asset ) );

		if ( IsImage( asset ) )
			return TryImageThumb( asset, out var image ) ? image : asset.AssetType?.Icon256;

		if ( asset.HasCachedThumbnail )
		{
			return asset.CachedThumbnail;
		}

		var fullPath = GetThumbnailFile( asset, false );
		if ( System.IO.File.Exists( fullPath ) )
		{
			var pix = Pixmap.FromFile( fullPath );
			asset.CachedThumbnail = pix;
			return asset.CachedThumbnail;
		}

		QueueThumbBuild( asset );

		while ( RenderQueue.Contains( asset ) || RenderingList.Contains( asset ) )
		{
			await Task.Delay( 100 );
		}

		asset.CachedThumbnail ??= asset.AssetType.Icon256;

		return asset.CachedThumbnail;
	}

	internal static Pixmap GetAssetThumb( Asset asset, bool generateIfNotInCache = true )
	{
		ArgumentNullException.ThrowIfNull( asset, nameof( asset ) );

		if ( IsImage( asset ) )
			return TryImageThumb( asset, out var image ) ? image : asset.AssetType?.Icon256;

		if ( asset.HasCachedThumbnail )
		{
			return asset.CachedThumbnail;
		}

		var fullPath = GetThumbnailFile( asset, false );
		if ( System.IO.File.Exists( fullPath ) )
		{
			var pix = Pixmap.FromFile( fullPath );
			asset.CachedThumbnail = pix;
			return asset.CachedThumbnail;
		}

		if ( asset.AssetType != null )
		{
			asset.CachedThumbnail = asset.AssetType.Icon256;
		}

		if ( generateIfNotInCache )
		{
			QueueThumbBuild( asset );
		}

		return asset.CachedThumbnail;
	}

	static bool IsImage( Asset asset )
	{
		if ( asset is null ) return false;
		if ( asset.AssetType == AssetType.ImageFile ) return true;

		var ext = Path.GetExtension( asset.Path );
		return AssetType.ImageFile is not null && AssetType.ImageFile.HasExtension( ext );
	}

	static bool TryImageThumb( Asset asset, out Pixmap pixmap )
	{
		pixmap = null;
		if ( !IsImage( asset ) ) return false;

		var path = asset.GetSourceFile( true );
		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) )
			path = asset.AbsolutePath;

		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) )
			return false;

		var ticks = File.GetLastWriteTimeUtc( path ).Ticks;

		if ( ImageThumbs.TryGetValue( path, out var cached ) && cached.Ticks == ticks && cached.Pixmap is { IsValid: true } )
		{
			pixmap = cached.Pixmap;
			asset.CachedThumbnail = pixmap;
			return true;
		}

		var loaded = Pixmap.FromFile( path );
		if ( loaded is null ) return false;

		pixmap = loaded;

		if ( loaded.Width > ImageEdge || loaded.Height > ImageEdge )
		{
			var resized = loaded.Resize( ImageEdge, ImageEdge );

			if ( resized is not null )
			{
				loaded.Release();
				pixmap = resized;
			}
		}

		if ( cached.Pixmap is not null && !ReferenceEquals( cached.Pixmap, pixmap ) )
			cached.Pixmap.Release();

		ImageThumbs[path] = (ticks, pixmap);
		asset.CachedThumbnail = pixmap;
		return true;
	}

	internal static void RefreshThumbnail( uint assetId )
	{
		var asset = AssetSystem.Get( assetId );
		if ( asset is null ) return;

		//Asked for on purpose, so whatever went wrong last time is worth trying again - the asset has usually just changed.
		Undrawable.Remove( asset.Path );

		RenderQueue.RemoveAll( x => x == asset );
		RenderQueue.Insert( 0, asset );
	}

	//Assets whose thumbnail failed to draw, so the expensive render isn't retried over and over
	static readonly HashSet<string> Undrawable = new( StringComparer.OrdinalIgnoreCase );

	//Extensions never thumbnailed by rendering (scenes would need a full load); they use the type icon
	static readonly HashSet<string> NoRenderedThumbnail = new( StringComparer.OrdinalIgnoreCase )
	{
		"scene",
	};

	//Whether this asset is one whose thumbnail we refuse to render - one we will not draw, or one nothing could.
	internal static bool IsRenderedThumbnailBlocked( Asset asset )
	{
		if ( asset is null ) return false;

		var ext = Path.GetExtension( asset.Path )?.TrimStart( '.' );
		if ( !string.IsNullOrEmpty( ext ) && NoRenderedThumbnail.Contains( ext ) )
			return true;

		var typeExt = asset.AssetType?.FileExtension;
		if ( !string.IsNullOrEmpty( typeExt ) && NoRenderedThumbnail.Contains( typeExt ) )
			return true;

		return !CanRenderThumbnail( asset, ext, typeExt );
	}

	//Whether anything could draw this asset a thumbnail, checked before the costly attempt
	static bool CanRenderThumbnail( Asset asset, string ext, string typeExt )
	{
		if ( asset.thumbnailOverride is not null ) return true;

		foreach ( var (_, attribute) in EditorTypeLibrary.GetTypesWithAttribute<AssetPreviewAttribute>() )
		{
			if ( string.IsNullOrEmpty( attribute.Extension ) ) continue;

			if ( string.Equals( attribute.Extension, ext, StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( attribute.Extension, typeExt, StringComparison.OrdinalIgnoreCase ) )
				return true;
		}

		if ( asset.AssetType?.ResourceType is { } resourceType &&
			resourceType.IsSubclassOf( typeof( Rinten.Resource ) ) &&
			resourceType.GetMethod( nameof( Rinten.Resource.RenderThumbnail ), [typeof( Rinten.Resource.ThumbnailOptions )] ) is { } draws &&
			draws.DeclaringType != typeof( Rinten.Resource ) )
			return true;

		foreach ( var (method, _) in EditorTypeLibrary.GetMethodsWithAttribute<Asset.ThumbnailRendererAttribute>() )
		{
			//The previews' own renderer is the one answered for above.
			if ( method.TypeDescription?.FullName != "Editor.Assets.AssetPreview" ) return true;
		}

		return false;
	}

	static List<Asset> RenderQueue = new();
	static List<Asset> RenderingList = new();

	internal static void DequeueThumbBuild( Asset asset )
	{
		if ( RenderingList.Contains( asset ) )
			return; //too late!

		RenderQueue.RemoveAll( x => x == asset );
	}

	internal static void QueueThumbBuild( Asset asset, bool add = true )
	{
		if ( RenderingList.Contains( asset ) )
			return;

		if ( Undrawable.Contains( asset.Path ) )
			return;

		if ( IsRenderedThumbnailBlocked( asset ) )
			return;

		if ( RenderQueue.RemoveAll( x => x == asset ) == 0 && !add )
			return;

		RenderQueue.Add( asset );
	}

	//How long the last thumbnail took to draw, and when it finished.
	private static System.Diagnostics.Stopwatch since;
	private static double lastCost;

	//After a thumbnail taking N ms, wait N ms before the next, so the editor stays responsive
	private const double Share = 1.0;

	//Never wait longer than this, however dear one of them was.
	private const double LongestWait = 250.0;

	internal static void Frame()
	{
		if ( since is not null && since.Elapsed.TotalMilliseconds < System.Math.Min( lastCost * Share, LongestWait ) )
			return;

		for ( int i = 0; i < RenderQueue.Count && RenderingList.Count < 1; i++ )
		{
			var asset = RenderQueue[i];
			RenderingList.Add( asset );
			RenderQueue.RemoveAt( i );

			_ = RenderThumbnailAsync( asset );

			i--;
		}
	}

	static async Task RenderThumbnailAsync( Asset asset )
	{
		await Task.Yield();

		var started = System.Diagnostics.Stopwatch.StartNew();

		try
		{
			using ( EditorUtility.DisableTextureStreaming() )
			{
				await asset.CacheAsync();
			}

			var pix = await RenderAssetThumb( asset );

			if ( pix is null )
			{
				Undrawable.Add( asset.Path );
				Log.Info( $"No thumbnail could be drawn for {asset.Path} - not trying again this session" );
			}

			if ( pix != null )
			{
				asset.CachedThumbnail = pix;

				var fullPath = GetThumbnailFile( asset, true );

				await Task.Run( () => pix.SavePng( fullPath ) );

				EditorEvent.RunInterface<AssetSystem.IEventListener>( x => x.OnAssetThumbGenerated( asset ) );
			}
		}
		catch ( System.Exception e )
		{
			//Remembered for the same reason a null result is: without this the next paint of the folder asks for the same thumbnail, which
			//loads the same asset, which throws again - for as long as the folder is open, which reads as an editor that has stopped responding.
			Undrawable.Add( asset.Path );

			Log.Warning( e, $"Exception when compiling thumbnail for {asset.Path}" );
		}
		finally
		{
			asset.Uncache();
			RenderingList.Remove( asset );

			//What it cost, and from when the next one's wait is measured.
			lastCost = started.Elapsed.TotalMilliseconds;
			since = System.Diagnostics.Stopwatch.StartNew();
		}
	}

	internal static async Task<Pixmap> RenderAssetThumb( Asset asset )
	{
		ThreadSafe.AssertIsMainThread();

		//If it's a resource, let it generate itself the thumbnail!
		{
			var resource = asset.LoadResource();
			var bitmap = resource?.RenderThumbnail( new() { Width = 256, Height = 256 } );
			if ( bitmap != null )
			{
				return Pixmap.FromBitmap( bitmap );
			}
		}


		if ( asset.thumbnailOverride != null )
		{
			return asset.thumbnailOverride;
		}

		//A file the asset system knows no type for has nothing to render and nothing to fall back on - reading its type below would throw instead.
		if ( asset.AssetType is null )
			return null;

		if ( IsRenderedThumbnailBlocked( asset ) )
			return null;

		var methods = EditorTypeLibrary.GetMethodsWithAttribute<Asset.ThumbnailRendererAttribute>();

		foreach ( var t in methods.OrderByDescending( x => x.Attribute.Priority ) )
		{
			try
			{
				ThreadSafe.AssertIsMainThread();

				var pixmap = t.Method.InvokeWithReturn<Task<Bitmap>>( null, [asset] );
				if ( pixmap is null ) continue;

				ThreadSafe.AssertIsMainThread();

				var r = await pixmap;
				if ( r is null ) continue;

				ThreadSafe.AssertIsMainThread();

				return Pixmap.FromBitmap( r );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when rendering thumb via {t.Method} ({e.Message})" );
			}
		}

		//Their preview system drew a thumbnail for anything it could load. What draws one now is whatever thumbnail renderer
		//the editor has for the type; there is no general fallback until the importer can load every kind of file on its own.
		return null;
	}
}
