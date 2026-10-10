using System.IO;

namespace Editor;

public record DiskLocation : AssetBrowser.Location
{
	public DiskLocation( string path ) : this( new DirectoryInfo( path ) )
	{
	}

	public DiskLocation( DirectoryInfo directory ) : base( null, null )
	{
		Path = directory.FullName;
		if ( Path.EndsWith( '\\' ) )
			Path = Path.TrimEnd( '\\' );

		Project = EditorUtility.FindProjectByDirectory( Path );

		RootPath = Project?.GetRootPath();
		RootTitle = Project?.Config.Title;
		if ( RootPath is null )
		{
			string v = Path.NormalizeFilename();
			string core = FileSystem.Root.GetFullPath( "/core/" );

			if ( v.StartsWith( core.NormalizeFilename().TrimEnd( '/' ) ) )
			{
				RootPath = core;
				RootTitle = "Core";
				Icon = "Folder";
				Type = AssetBrowser.LocationType.Assets;
			}
		}

		if ( string.Equals( RootPath, Path, StringComparison.OrdinalIgnoreCase ) )
		{
			Name = RootTitle;
			IsRoot = true;
			RelativePath = "";

			if ( Project != null )
			{
				Icon = Project.Config.Type switch
				{
					"map" => "public",
					"game" => "sports_esports",
					"content" => "perm_media",
					"tool" => "hardware",
					_ => "Folder_Special"
				};
			}
		}
		else
		{
			Name = directory.Name;

			if ( RootPath is null )
			{
				RelativePath = Path;
			}
			else
			{
				RelativePath = System.IO.Path.GetRelativePath( RootPath, Path ).NormalizeFilename( false, false );

				//determine where this folder is located, as it determines how we treat it and what we expect to be inside
				string initialFolder = RelativePath;
				int seperatorIdx = RelativePath.IndexOf( '/' );
				if ( seperatorIdx > 0 )
				{
					initialFolder = RelativePath.Substring( 0, seperatorIdx );
				}

				if ( Type == AssetBrowser.LocationType.Generic )
				{
					Type = initialFolder.ToLower() switch
					{
						"assets" => AssetBrowser.LocationType.Assets,
						"editor" or "code" or "unittests" => AssetBrowser.LocationType.Code,
						"localization" => AssetBrowser.LocationType.Localization,
						_ => AssetBrowser.LocationType.Generic,
					};
				}

				Icon = "folder";

				if ( seperatorIdx == -1 && Project is not null )
				{
					ContentsIcon = DirectoryEntry.GetUniqueIcon( Name ) ?? null;
					Name = Name.ToTitleCase();
				}
			}
		}
	}

	public override bool IsValid()
	{
		return Directory.Exists( Path );
	}

	public override IEnumerable<AssetBrowser.Location> GetDirectories()
	{
		if ( !Directory.Exists( Path ) )
			yield break;

		foreach ( var subDir in Directory.GetDirectories( Path ) )
		{
			var dir = new DirectoryInfo( subDir );

			if ( dir.Attributes.HasFlag( FileAttributes.Hidden ) )
				continue;

			string name = dir.Name.ToLower();
			if ( name.StartsWith( '.' ) ) continue;
			if ( name.StartsWith( '_' ) ) continue;

			if ( name.Equals( "config" ) ) continue;
			if ( name.Equals( "obj" ) ) continue;

			//Hide obj/ folder in code directories
			if ( Type is AssetBrowser.LocationType.Code )
			{
				if ( name.Equals( "obj" ) ) continue;
				if ( name.Equals( "properties" ) ) continue;
			}

			if ( IsRoot && name.Equals( "libraries" ) )
				continue;

			yield return new DiskLocation( dir );
		}
	}

	public override IEnumerable<FileInfo> GetFiles()
	{
		foreach ( var filePath in Directory.GetFiles( Path ) )
		{
			var file = new FileInfo( filePath );

			if ( file.Attributes.HasFlag( FileAttributes.Hidden ) )
				continue;

			if ( file.Name.StartsWith( '.' ) ) continue;

			yield return file;
		}
	}
}
