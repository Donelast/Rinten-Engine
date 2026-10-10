using System.IO;

namespace Editor;

public record EverythingLocation : AssetBrowser.Location
{
	public override bool IsAggregate => true;

	public EverythingLocation() : base( "Everything", "public" )
	{
		Path = "@everything";
	}

	public override bool CanGoUp() => false;
	public override IEnumerable<AssetBrowser.Location> GetDirectories() => Enumerable.Empty<AssetBrowser.Location>();

	public override IEnumerable<FileInfo> GetFiles()
	{
		string projectPath = Project.Current.GetAssetsPath().NormalizeFilename( false );

		var menuProject = EditorUtility.Projects.GetAll().FirstOrDefault( x => x.Config.Ident == "menu" );
		string menuPath = menuProject?.GetAssetsPath().NormalizeFilename( false );

		foreach ( var asset in AssetSystem.All.OrderBy( x => x.Name ) )
		{
			bool isCloud = asset.AbsolutePath.Contains( Project.CloudPath );
			if ( isCloud ) continue;

			if ( menuPath is not null && menuProject != Project.Current )
			{
				bool isMenu = asset.AbsolutePath.StartsWith( menuPath );
				if ( isMenu ) continue;
			}

			yield return new FileInfo( asset.AbsolutePath );
		}
	}
}
