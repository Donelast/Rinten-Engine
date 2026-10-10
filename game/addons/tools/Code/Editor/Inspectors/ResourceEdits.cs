namespace Editor.Inspectors;

//Writing an edited resource back to its file, the one way every resource page does it.
static class ResourceEdits
{
	/// <summary>Whether the file may be written at all - a cloud package cannot, and neither can a file that is not there.</summary>
	public static bool IsReadOnly( Asset asset )
		=> AssetSystem.IsCloudInstalled( asset.Package ) || !asset.HasSourceFile;

	/// <summary>Writes the resource out and keeps the file's before and after, so the edit can be stepped back.</summary>
	public static void Write( Asset asset, GameResource resource )
	{
		var file = asset?.GetSourceFile( true );
		var before = Read( file );

		asset?.SaveToDisk( resource );

		ResourceHistory.Record( file, "Edit", before, Read( file ) );
	}

	static string Read( string file )
	{
		if ( string.IsNullOrEmpty( file ) ) return null;

		try
		{
			return System.IO.File.Exists( file ) ? System.IO.File.ReadAllText( file ) : null;
		}
		catch
		{
			return null;
		}
	}
}
