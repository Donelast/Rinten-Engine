using System.IO;

namespace Rinten;

public sealed partial class Project
{
	public const string ThumbMetaKey = "Thumb";

	static readonly string[] DefaultThumbNames =
	[
		"icon.png",
		"icon.jpg",
		"icon.jpeg",
		"icon.webp",
		"icon.tga",
	];

	static readonly HashSet<string> ThumbExtensions = new( StringComparer.OrdinalIgnoreCase )
	{
		".png", ".jpg", ".jpeg", ".webp", ".tga"
	};

	/// <summary>Absolute path to the project's image, or null if none is on disk.</summary>
	public string GetThumbPath()
	{
		if ( RootDirectory is null )
			return null;

		var root = Path.GetFullPath( RootDirectory.FullName );
		if ( !root.EndsWith( Path.DirectorySeparatorChar ) && !root.EndsWith( Path.AltDirectorySeparatorChar ) )
			root += Path.DirectorySeparatorChar;

		if ( Config?.TryGetMeta<string>( ThumbMetaKey, out var relative ) == true
			&& !string.IsNullOrWhiteSpace( relative ) )
		{
			var specified = Path.GetFullPath( Path.Combine( RootDirectory.FullName, relative ) );
			if ( specified.StartsWith( root, StringComparison.OrdinalIgnoreCase ) && File.Exists( specified ) )
				return specified;
		}

		foreach ( var name in DefaultThumbNames )
		{
			var path = Path.Combine( root, name );
			if ( File.Exists( path ) )
				return path;
		}

		return null;
	}

	/// <summary>Copies an image into the project root and stores it as the project image.</summary>
	public bool SetThumbFromFile( string sourcePath )
	{
		if ( string.IsNullOrWhiteSpace( sourcePath ) || RootDirectory is null || !File.Exists( sourcePath ) )
			return false;

		var ext = Path.GetExtension( sourcePath );
		if ( string.IsNullOrEmpty( ext ) || !ThumbExtensions.Contains( ext ) )
			return false;

		var destName = "icon" + ext.ToLowerInvariant();
		var destPath = Path.Combine( RootDirectory.FullName, destName );
		var sourceFull = Path.GetFullPath( sourcePath );
		var destFull = Path.GetFullPath( destPath );

		if ( !string.Equals( sourceFull, destFull, StringComparison.OrdinalIgnoreCase ) )
			File.Copy( sourceFull, destFull, overwrite: true );

		foreach ( var name in DefaultThumbNames )
		{
			if ( string.Equals( name, destName, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var leftover = Path.Combine( RootDirectory.FullName, name );
			if ( File.Exists( leftover ) )
				File.Delete( leftover );
		}

		Config.SetMeta( ThumbMetaKey, destName );
		Save();
		UpdateMockPackage();
		return true;
	}
}
