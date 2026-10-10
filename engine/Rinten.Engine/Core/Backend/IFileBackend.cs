using System.Collections.Concurrent;

namespace Rinten;

//Content location registry (idents to folders, path redirects); not file I/O
internal interface IFileBackend
{
	//Register a project's asset folder under an ident.
	void AddProjectPath( string ident, string fullPath );

	//Register a cloud/transient asset folder under an ident.
	void AddCloudPath( string ident, string fullPath );

	//Drop all registered project paths.
	void ResetProjectPaths( bool includeCloudAssets );

	//Redirect a virtual path to a file elsewhere on disk.
	void AddSymLink( string localPath, string pathId, string realPath );

	void RemoveSymLink( string localPath, string pathId );

	//The real path a virtual path redirects to, or null.
	string GetSymLink( string localPath, string pathId );
}

//Our own content registry: which folder each ident is, and what redirects where.
internal sealed class CoreFileBackend : IFileBackend
{
	private readonly ConcurrentDictionary<string, string> projectPaths = new( StringComparer.OrdinalIgnoreCase );
	private readonly ConcurrentDictionary<string, string> cloudPaths = new( StringComparer.OrdinalIgnoreCase );
	private readonly ConcurrentDictionary<string, string> symLinks = new( StringComparer.OrdinalIgnoreCase );

	private static string LinkKey( string localPath, string pathId ) => $"{pathId}:{localPath}";

	internal IReadOnlyDictionary<string, string> ProjectPaths => projectPaths;
	internal IReadOnlyDictionary<string, string> CloudPaths => cloudPaths;
	internal int SymLinkCount => symLinks.Count;

	public void AddProjectPath( string ident, string fullPath )
	{
		projectPaths[ident] = fullPath;
	}

	public void AddCloudPath( string ident, string fullPath )
	{
		cloudPaths[ident] = fullPath;
	}

	public void ResetProjectPaths( bool includeCloudAssets )
	{
		projectPaths.Clear();
		if ( includeCloudAssets ) cloudPaths.Clear();
	}

	public void AddSymLink( string localPath, string pathId, string realPath )
	{
		symLinks[LinkKey( localPath, pathId )] = realPath;
	}

	public void RemoveSymLink( string localPath, string pathId )
	{
		symLinks.TryRemove( LinkKey( localPath, pathId ), out _ );
	}

	public string GetSymLink( string localPath, string pathId )
	{
		if ( symLinks.TryGetValue( LinkKey( localPath, pathId ), out var real ) )
			return real;

		//A link the native side knows about but we do not means something registered behind our back; fall through rather than pretend it does not exist.
		return null;
	}
}
