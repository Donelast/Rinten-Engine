using System.IO;

namespace Rinten;

internal class LauncherPreferences
{
	private static CookieContainer _cookie;

	//Created lazily: panels read these while building, before anyone could load them explicitly
	public static CookieContainer Cookie => _cookie ??= new CookieContainer( "launcher" );

	public static string DefaultProjectLocation
	{
		get => Cookie.GetString( "DefaultProjectLocation", Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.MyDocuments ), "Rinten Projects" ) );
		set => Cookie.SetString( "DefaultProjectLocation", value );
	}

	//The GitHub repository engine versions are installed from, as owner/name; the environment variable wins
	public static string ReleasesRepository
	{
		get => Environment.GetEnvironmentVariable( "RINTEN_RELEASES_REPO" )
			?? Cookie.GetString( "ReleasesRepository", "somethink000/Rinten-Engine" );
		set => Cookie.SetString( "ReleasesRepository", value );
	}

	public static void Save()
	{
		_cookie?.Save();
	}
}
