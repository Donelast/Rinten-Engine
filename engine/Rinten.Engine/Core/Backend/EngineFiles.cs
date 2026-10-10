namespace Rinten;

//The engine's content registry.
internal static class EngineFiles
{
	private static readonly IFileBackend backend = new CoreFileBackend();

	/// <summary>Register a project's asset folder under an ident.</summary>
	public static void AddProjectPath( string ident, string fullPath ) => backend.AddProjectPath( ident, fullPath );

	/// <summary>Register a cloud/transient asset folder under an ident.</summary>
	public static void AddCloudPath( string ident, string fullPath ) => backend.AddCloudPath( ident, fullPath );

	/// <summary>Drop all registered project paths.</summary>
	public static void ResetProjectPaths( bool includeCloudAssets ) => backend.ResetProjectPaths( includeCloudAssets );

	/// <summary>Redirect a virtual path to a file elsewhere on disk.</summary>
	public static void AddSymLink( string localPath, string pathId, string realPath ) => backend.AddSymLink( localPath, pathId, realPath );

	public static void RemoveSymLink( string localPath, string pathId ) => backend.RemoveSymLink( localPath, pathId );

	/// <summary>The real path a virtual path redirects to, or null.</summary>
	public static string GetSymLink( string localPath, string pathId ) => backend.GetSymLink( localPath, pathId );

	[ConCmd( "filesystem_paths", ConVarFlags.Protected, Help = "List the content paths the engine has registered" )]
	public static void PrintPaths()
	{
		Log.Info( $"Filesystem registry - {(backend is CoreFileBackend ? "our core" : "s&box")}" );

		if ( backend is not CoreFileBackend core )
		{
			Log.Info( "  s&box owns the registry and does not expose its contents." );
			Log.Info( "  Nothing is recorded on our side, so there is nothing to list here." );
			return;
		}

		Log.Info( $"  project paths : {core.ProjectPaths.Count}" );
		foreach ( var (ident, path) in core.ProjectPaths.OrderBy( x => x.Key ) )
			Log.Info( $"    {ident,-28} {path}" );

		Log.Info( $"  cloud paths   : {core.CloudPaths.Count}" );
		foreach ( var (ident, path) in core.CloudPaths.OrderBy( x => x.Key ) )
			Log.Info( $"    {ident,-28} {path}" );

		Log.Info( $"  symlinks      : {core.SymLinkCount}" );
	}
}
