using System.IO;

namespace Rinten;

/// <summary>Drops caches built from a changed project file so the next request reads it again</summary>
public static class ResourceReload
{
	/// <summary>One file changed. Safe to call for anything: a path this knows nothing about does nothing.</summary>
	public static void Changed( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return;

		//Everything under here is main-thread work - the caches it drops are walked by the draw - and a watcher can arrive on any thread.
		if ( !ThreadSafe.IsMainThread ) return;

		var path = Source( resourcePath );
		var extension = Path.GetExtension( path ).ToLowerInvariant();

		switch ( extension )
		{
			case ".mdl":
			case ".vmdl":
				//Drops the device mesh and tells every renderer standing on it - see Model.OnReloaded.
				Model.ReloadFromDisk( path );
				return;

			case ".mat":
			case ".vmat":
				//Three caches deep: what the file says, the material loaded from it, and the descriptor set on the device that the meshes hold.
				CoreImportedMaterial.Forget();
				Material.ReloadFromDisk( path );
				CoreResources.Forget( path );
				return;

			case ".shader":
			case ".slang":
				//An include takes every shader with it - see CoreResources.Forget.
				CoreResources.Forget( path );
				return;

			case ".snd":
			case ".vsnd":
			case ".wav":
			case ".mp3":
			case ".flac":
			case ".ogg":
				Rinten.Audio.CoreSounds.Forget( path );
				SoundFile.Reread( path );
				return;

			default:
				//A picture, whatever it was written as. The compiled .tex_c beside it is rebuilt on its own - it is compared
				//against the image's own timestamp - so only what is on the device has to go. See CoreSceneRenderer.Compiled.
				if ( IsPicture( extension ) ) CoreResources.Forget( path );
				return;
		}
	}

	/// <summary>Marks a file as rewritten in a way nothing depends on, so watchers skip it (e.g. model graph edits)</summary>
	public static void Unchanged( string absolutePath )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) ) return;

		var full = OnDisk( absolutePath );

		try
		{
			var written = File.GetLastWriteTimeUtc( full );

			lock ( unchanged ) unchanged[full] = written;
		}
		catch ( IOException )
		{
			//Gone already. Whatever comes of that is a change worth seeing.
		}
	}

	/// <summary>Whether the file on disk is still the one <see cref="Unchanged"/> was told about - the editor's own write, seen coming back.</summary>
	public static bool IsUnchanged( string absolutePath )
	{
		if ( string.IsNullOrWhiteSpace( absolutePath ) ) return false;

		var full = OnDisk( absolutePath );

		DateTime written;

		lock ( unchanged )
		{
			if ( !unchanged.TryGetValue( full, out written ) ) return false;
		}

		try
		{
			return File.Exists( full ) && File.GetLastWriteTimeUtc( full ) == written;
		}
		catch ( IOException )
		{
			return false;
		}
	}

	private static readonly Dictionary<string, DateTime> unchanged = new( StringComparer.Ordinal );

	private static string OnDisk( string path ) => Path.GetFullPath( path ).Replace( '\\', '/' );

	//Maps a compiled name (foo.mdl_c) back to the source path it was built from
	private static string Source( string resourcePath )
	{
		var path = resourcePath.Replace( '\\', '/' ).TrimStart( '/' );

		return path.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) ? path[..^2] : path;
	}

	//Whether an extension is one the texture path reads.
	private static bool IsPicture( string extension ) => extension switch
	{
		".tex" or ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp"
			or ".psd" or ".gif" or ".exr" or ".hdr" or ".dds" or ".webp" => true,
		_ => false,
	};
}
