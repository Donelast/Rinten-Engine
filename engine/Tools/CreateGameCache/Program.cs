using Rinten;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Rinten.CreateGameCache;

public static class Program
{
	const string CacheFolder = "gamecache";

	static DirectoryInfo CacheDirectory;

	static List<Task> tasks = new();

	public static async Task Main( string[] args )
	{
		var dir = Rinten.EngineEnvironment.EngineFolder;
		var cachePath = System.IO.Path.Combine( dir, CacheFolder );
		CacheDirectory = new DirectoryInfo( cachePath );

		CacheDirectory.Create();

		Rinten.Api.Init();

		await FindAndInstallPackage( "type:model sort:popular org:facepunch", 200 );
		await FindAndInstallPackage( "type:model sort:spawns org:facepunch", 200 );

		await InstallPackage( "facepunch.ss1" );
		await InstallPackage( "facepunch.sandbox" );
		await InstallPackage( "facepunch.construct" );
		await InstallPackage( "facepunch.flatgrass" );
		await InstallPackage( "facepunch.square" );
		await InstallPackage( "facepunch.testbed" );
		await InstallPackage( "facepunch.hc1" );
		await InstallPackage( "facepunch.depot" );
		await InstallPackage( "facepunch.construct23" );

		await Task.WhenAll( tasks );
	}

	static async Task FindAndInstallPackage( string query, int max )
	{
		Console.WriteLine( $"{query}" );
		var result = await Package.FindAsync( query, max );
		foreach ( var package in result.Packages )
		{
			await InstallPackage( package.FullIdent );
		}
	}

	static async Task InstallPackage( string packageName )
	{
		Console.WriteLine( $"{packageName}" );

		var package = await Rinten.Package.Fetch( packageName, false );

		await package.Revision.DownloadManifestAsync();

		foreach ( var file in package.Revision.Manifest.Files )
		{
			tasks.Add( DownloadFile( file ) );
		}
	}

	//Only allow 16 downloads at a time
	static SemaphoreSlim throttler = new SemaphoreSlim( 16 );

	static async Task DownloadFile( Rinten.ManifestSchema.File file )
	{
		await throttler.WaitAsync();

		try
		{
			string filename = AssetDownloadCache.CreateGameCacheFilename( file.Path, file.Crc );
			var path = Path.Combine( CacheDirectory.FullName, filename );

			if ( System.IO.File.Exists( path ) )
			{
				var info = new FileInfo( path );
				if ( info.Length == file.Size ) return;
			}

			Console.WriteLine( $"{file.Path}" );
			await Rinten.Utility.Web.DownloadFile( file.Url, path, default, default );
		}
		finally
		{
			throttler.Release();
		}
	}
}
