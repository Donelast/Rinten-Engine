using System.Threading;

namespace Rinten;

internal sealed class PackageRevision : Package.IRevision
{
	long Package.IRevision.VersionId => AssetVersionId;
	long Package.IRevision.FileCount => FileCount;
	long Package.IRevision.TotalSize => TotalSize;
	DateTimeOffset Package.IRevision.Created => Created;
	int Package.IRevision.EngineVersion => EngineVersion;
	ManifestSchema Package.IRevision.Manifest => _manifest;

	public long FileCount { get; set; }
	public long AssetVersionId { get; set; }
	public long TotalSize { get; set; }
	public string ManifestUrl { get; set; }
	public string Summary { get; set; }
	public DateTimeOffset Created { get; set; }
	public int EngineVersion { get; set; }
	public string Meta { get; set; }
	public string Changes { get; set; }

	ManifestSchema _manifest;

	/// <summary>The manifest might not be immediately available until you've downloaded it</summary>
	public async Task DownloadManifestAsync( CancellationToken token )
	{
		if ( _manifest != null )
			return;

		//empty manifest fallback
		_manifest = new ManifestSchema();

		if ( string.IsNullOrEmpty( ManifestUrl ) )
			return;

		try
		{
			_manifest = await Utility.Web.DownloadJson<ManifestSchema>( ManifestUrl );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Manifest: Couldn't deserialize schema ({e.Message})" );
		}
	}

	internal static PackageRevision FromDto( Rinten.Services.PackageVersion x )
	{
		return new PackageRevision
		{
			FileCount = x.FileCount,
			AssetVersionId = x.AssetVersionId,
			TotalSize = x.TotalSize,
			ManifestUrl = x.ManifestUrl,
			Created = x.Created,
			Summary = x.Changes,
			EngineVersion = x.EngineVersion,
			Meta = x.Meta,
			Changes = x.Changes,
		};
	}
}
