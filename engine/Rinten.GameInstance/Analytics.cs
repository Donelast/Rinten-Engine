using Rinten.Diagnostics;
using System;
using static Rinten.Diagnostics.PerformanceStats;

internal static class Analytics
{
	static RealTimeUntil timeUntilNextUpdate;

	static string gameIdent;
	static string gameVersion;
	static string mapIdent;
	static string[] contentIdent;

	static int lastHash;

	static RealTimeUntil timeUntilThink;


	internal static void Tick()
	{
		if ( timeUntilThink > 0 ) return;

		//must be a tool or something?
		if ( Application.IsHeadless ) return;

		timeUntilThink = Random.Shared.Float( 2, 4 );

		gameIdent = Application.GameIdent;
		gameVersion = Application.GamePackage?.Revision?.VersionId.ToString() ?? "";
		mapIdent = Application.MapPackage?.FullIdent ?? "";

		//get a list of installed packages
		contentIdent = PackageManager.ActivePackages
								.Where( x => x.Package is RemotePackage )
								.Select( x => x.Package.FullIdent )
								.ToArray();

		CheckHash();
		TryUpdateActivity();
	}

	static void CheckHash()
	{
		var hc = new HashCode();

		hc.Add( gameIdent );
		hc.Add( mapIdent );

		for ( int i = 0; i < contentIdent.Length; i++ )
			hc.Add( contentIdent[i] );

		if ( lastHash == hc.ToHashCode() )
			return;

		lastHash = hc.ToHashCode();
		timeUntilNextUpdate = 1.0f; //debounce
	}

	internal static void TryUpdateActivity()
	{
		if ( timeUntilNextUpdate > 0.0f )
			return;

		timeUntilNextUpdate = 60.0f * 1.0f;

		Task.Run( () => Api.Activity.UpdateActivity( gameIdent, gameVersion, mapIdent, contentIdent ) );
	}
}
