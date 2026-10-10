using System.IO;

namespace Editor;

public record MountLocation : AssetBrowser.Location
{
	private readonly string ident;
	public Rinten.Mounting.BaseGameMount Source => Rinten.Mounting.Directory.Get( ident );

	public override bool IsValid() => Source.IsMounted && base.IsValid();

	public MountLocation( Rinten.Mounting.BaseGameMount source, string path = null ) : base( source.Title, "supervisor_account" )
	{
		ident = source.Ident;
		RootPath = $"mount://{Source.Ident}";
		Path = path ?? "";
		Type = AssetBrowser.LocationType.Assets;

		IsRoot = path == null || path == RootPath;
		RootTitle = source.Title;

		if ( IsRoot )
		{
			Name = source.Title;
			Icon = "save";
			Path = RootPath;
			RelativePath = "/";
		}
		else
		{
			Name = path.Split( '/' ).LastOrDefault();
			Icon = "folder";
			RelativePath = System.IO.Path.GetRelativePath( RootPath, Path );
		}
	}

	public override IEnumerable<AssetBrowser.Location> GetDirectories()
	{
		var basePath = Path ?? $"mount://{Source.Ident}";
		basePath += '/';

		return Source.Resources
				.Select( x => x.Path )
				.Where( p => p.StartsWith( basePath, StringComparison.OrdinalIgnoreCase ) )
				.Select( p => System.IO.Path.GetDirectoryName( p.Substring( basePath.Length ) ) )
				.Select( p => p.Replace( '\\', '/' ) )
				.Select( p => p.Split( '/' ).FirstOrDefault() )
				.Where( d => !string.IsNullOrWhiteSpace( d ) )
				.Distinct().Select( x => new MountLocation( Source, basePath + x ) );
	}

	public override IEnumerable<FileInfo> GetFiles()
	{
		var basePath = Path ?? $"mount://{Source.Ident}";
		basePath += '/';

		foreach ( var f in Source.Resources )
		{
			if ( !f.Path.StartsWith( basePath ) ) continue;

			var relative = f.Path.Substring( basePath.Length );
			if ( relative.Contains( '/' ) ) continue;

			var file = new FileInfo( f.Path );
			yield return file;
		}
	}
}
