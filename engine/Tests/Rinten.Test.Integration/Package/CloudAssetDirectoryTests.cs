using System;
using System.Collections.Generic;
using System.IO;
using Editor;
using Rinten.Services;

namespace PackageTests;

//CloudAssetDirectory (LiteDB under .rinten/cloud) stores real packages and reloads them after reopen
[TestClass]
public class CloudAssetDirectoryTest
{
	//A fixed timestamp with second precision, so date fields survive LiteDB's date serialization without needing tolerance checks.
	static DateTimeOffset Stamp = new( 2021, 6, 1, 12, 0, 0, TimeSpan.Zero );

	//Per-test scratch folder for the LiteDB database files.
	static string TestDir => Path.Combine( Environment.CurrentDirectory, ".rinten", "test_cloud_asset_directory" );

	//Wipes and recreates the scratch folder before each test so on-disk databases never leak between tests.
	[TestInitialize]
	public void TestInitialize()
	{
		if ( Directory.Exists( TestDir ) )
			Directory.Delete( TestDir, true );

		Directory.CreateDirectory( TestDir );
	}

	//Returns a unique database path within the scratch folder for the given test.
	static string DbPath( string name ) => Path.Combine( TestDir, $"{name}.db" );

	//Builds a remote package with a single revision and no registered files. The revision's
	//Meta carries the PrimaryAsset so we can prove the JSON metadata survives the round-trip.
	static RemotePackage MakePackage( string org, string ident, string typeName, long versionId, string meta = "{}" )
	{
		return new RemotePackage
		{
			Org = new Package.Organization { Ident = org, Title = $"{org} org", Thumb = $"https://example.com/{org}.png" },
			Ident = ident,
			Title = $"{ident} title",
			Summary = $"{ident} summary",
			Thumb = $"https://example.com/{ident}.png",
			TypeName = typeName,
			Tags = new[] { "alpha", "beta" },
			Public = true,
			Created = Stamp,
			Updated = Stamp,
			Version = new PackageRevision
			{
				AssetVersionId = versionId,
				FileCount = 0,
				TotalSize = 0,
				Created = Stamp,
				EngineVersion = 7,
				ManifestUrl = "https://example.com/manifest.json",
				Meta = meta,
				Changes = "initial",
			},
		};
	}

	//The headline scenario: fetch a real package of every asset-party type from the cloud, store them all in a directory, close
	//it, then re-open from disk and confirm each package loads back with its identity, type and revision intact, against live data.
	[TestMethod]
	[TestCategory( "LiveBackend" )]
	public async Task FetchEveryType_StoreCloseReopen_LoadsBack()
	{
		var idents = new[]
		{
			"facepunch.flatgrass",      //map
			"facepunch.sandbox",        //game
			"facepunch.watermelon",     //model
			"facepunch.glass_a",        //material
			"igrotronika.explosion2",   //sound
			"facepunch.shatterglass",   //library
			"ducksworkshop.mobbosstop", //clothing
			"wmfg.powerkart",           //prefab
		};

		var expected = new List<(string ident, string typeName, long version)>();
		var path = DbPath( "every_type" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			foreach ( var ident in idents )
			{
				var package = await Package.FetchAsync( ident, false );

				Assert.IsNotNull( package, $"Failed to fetch {ident} from the cloud" );
				Assert.IsNotNull( package.Revision, $"{ident} came back without a revision" );

				directory.AddPackage( package );

				expected.Add( (package.FullIdent, package.TypeName, package.Revision.VersionId) );
			}
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			foreach ( var (ident, typeName, version) in expected )
			{
				var loaded = directory.FindPackage( ident );

				Assert.IsNotNull( loaded, $"{ident} did not survive the save/reopen round-trip" );
				Assert.AreEqual( ident, loaded.FullIdent );
				Assert.AreEqual( typeName, loaded.TypeName );
				Assert.IsNotNull( loaded.Revision, $"{ident} lost its revision on reopen" );
				Assert.AreEqual( version, loaded.Revision.VersionId );
			}
		}
	}

	//Reopened packages keep the stored fields: identity, display fields, revision and metadata JSON
	[TestMethod]
	public void StorePackage_PreservesStoredFields()
	{
		var package = MakePackage( "clouddirtest", "plain", "material", 99, "{\"PrimaryAsset\":\"materials/plain.vmat\"}" );
		var path = DbPath( "plain" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.AddPackage( package );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			var loaded = directory.FindPackage( package.FullIdent );

			Assert.IsNotNull( loaded );
			Assert.AreEqual( package.FullIdent, loaded.FullIdent );
			Assert.AreEqual( package.Org.Ident, loaded.Org.Ident );
			Assert.AreEqual( package.Org.Title, loaded.Org.Title );
			Assert.AreEqual( package.Ident, loaded.Ident );
			Assert.AreEqual( package.Title, loaded.Title );
			Assert.AreEqual( package.Summary, loaded.Summary );
			Assert.AreEqual( package.Thumb, loaded.Thumb );
			Assert.AreEqual( package.Org.Thumb, loaded.Org.Thumb );
			Assert.AreEqual( package.TypeName, loaded.TypeName );
			Assert.AreEqual( Stamp.ToUnixTimeSeconds(), loaded.Created.ToUnixTimeSeconds() );
			CollectionAssert.AreEqual( package.Tags, loaded.Tags );

			Assert.IsNotNull( loaded.Revision );
			Assert.AreEqual( 99L, loaded.Revision.VersionId );
			Assert.AreEqual( 7, loaded.Revision.EngineVersion );
			Assert.AreEqual( "materials/plain.vmat", loaded.PrimaryAsset );
		}
	}

	//FindPackage(ident) returns the package from the in-memory cache within the same
	//session, and GetPackages reflects the addition, before any save or reopen.
	[TestMethod]
	public void AddPackage_IsFoundInSameSession()
	{
		var package = MakePackage( "clouddirtest", "samesession", "addon", 5 );
		var path = DbPath( "same_session" );

		using var directory = new CloudAssetDirectory( path );
		directory.AddPackage( package );

		var found = directory.FindPackage( package.FullIdent );

		Assert.IsNotNull( found );
		Assert.AreEqual( package.FullIdent, found.FullIdent );
		Assert.AreEqual( 1, directory.GetPackages().Count );
	}

	//RemovePackage drops the package from both the in-memory cache and the on-disk database, so a later reopen no longer finds it.
	[TestMethod]
	public void RemovePackage_RemovesFromCacheAndDisk()
	{
		var package = MakePackage( "clouddirtest", "removable", "addon", 8 );
		var path = DbPath( "removable" );

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.AddPackage( package );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			directory.RemovePackage( package );
			Assert.IsNull( directory.FindPackage( package.FullIdent ), "Package still present in cache after removal" );
		}

		using ( var directory = new CloudAssetDirectory( path ) )
		{
			Assert.IsNull( directory.FindPackage( package.FullIdent ), "Package came back after reopen, so it was not removed from disk" );
		}
	}

	//AddFile registers files against a package's current revision and GetPackageFiles returns them. Exercised
	//within a single session so it does not depend on the files existing on disk for the reopen validation.
	[TestMethod]
	public void AddFile_GetPackageFiles_ReturnsRegisteredFiles()
	{
		var package = MakePackage( "clouddirtest", "withfiles", "addon", 3 );
		var path = DbPath( "with_files" );

		using var directory = new CloudAssetDirectory( path );
		directory.AddPackage( package );

		directory.AddFile( "/cloud/withfiles/model.vmdl_c", "crc-a", 100, package );
		directory.AddFile( "/cloud/withfiles/texture.vtex_c", "crc-b", 200, package );

		var files = directory.GetPackageFiles( package ).ToList();

		Assert.AreEqual( 2, files.Count );
	}

	//Package lookup from file
	[TestMethod]
	public void AddFile_GetPackages()
	{
		var path = DbPath( "get_package" );
		using var directory = new CloudAssetDirectory( path );

		var packageA = MakePackage( "clouddirtest", "one", "material", 99 );
		var packageB = MakePackage( "clouddirtest", "two", "material", 99, "{\"PrimaryAsset\":\"materials/plain.vmat\"}" );
		var packageC = MakePackage( "clouddirtest", "three", "material", 99 );

		directory.AddPackage( packageA );
		directory.AddPackage( packageB );
		directory.AddPackage( packageC );

		directory.AddFile( "materials/plain.vmat", "crc-a", 100, packageA );
		directory.AddPackageLink( "materials/plain.vmat", packageB );
		directory.AddPackageLink( "materials/plain.vmat", packageC );

		//FindPackage - multiple are registed, but should return the one where it's a PrimaryAsset
		var package = directory.FindPackage( $"c:/projects/mine{Project.CloudPath}materials/plain.vmat", "materials/plain.vmat" );
		Assert.AreEqual( package, packageB );

		//FindPackages returns all packages that share the file
		var packages = directory.FindPackages( $"c:/projects/mine{Project.CloudPath}materials/plain.vmat", "materials/plain.vmat" );
		Assert.AreEqual( packages.Count(), 3 );
	}

	//When encountering a shared file with different versions, AddFile keeps the file from the package with the newer revision
	[TestMethod]
	public void AddFile_VersionConflict()
	{
		var path = DbPath( "add_ver_conflict" );
		using var directory = new CloudAssetDirectory( path );

		var packageB = MakePackage( "clouddirtest", "two", "material", 99 );
		Stamp = Stamp.AddSeconds( 1 ); //make sure the revision is newer
		var packageA = MakePackage( "clouddirtest", "one", "material", 99 );

		directory.AddPackage( packageA );
		directory.AddPackage( packageB );

		Assert.IsTrue( directory.AddFile( "materials/plain.vmat", "crc-a", 100, packageA ) );
		Assert.IsFalse( directory.AddFile( "materials/plain.vmat", "crc-b", 200, packageB ) );

		Assert.IsTrue( directory.TryGetFile( "materials/plain.vmat", out var file ) );
		Assert.AreEqual( file.Size, 100 ); //the file from the newer revision is what's kept
	}

	//Re-adding a package at a newer revision purges the file records tied to the
	//old revision, keeping the directory in sync with what is actually installed.
	[TestMethod]
	public void AddPackage_NewerRevision_PurgesStaleFiles()
	{
		var v1 = MakePackage( "clouddirtest", "versioned", "addon", 1 );
		var path = DbPath( "stale_files" );

		using var directory = new CloudAssetDirectory( path );
		directory.AddPackage( v1 );
		directory.AddFile( "/cloud/versioned/old.vmdl_c", "crc-old", 100, v1 );

		Assert.AreEqual( 1, directory.GetPackageFiles( v1 ).Count() );

		var v2 = MakePackage( "clouddirtest", "versioned", "addon", 2 );
		directory.AddPackage( v2 );

		Assert.AreEqual( 0, directory.GetPackageFiles( v1 ).Count(), "Old-revision files were not purged when the package updated" );
	}

	//FindPackage returns null for idents that are empty or cannot be parsed, rather than throwing.
	[TestMethod]
	public void FindPackage_InvalidIdent_ReturnsNull()
	{
		var path = DbPath( "invalid_ident" );
		using var directory = new CloudAssetDirectory( path );

		string nullIdent = null;

		Assert.IsNull( directory.FindPackage( nullIdent ) );
		Assert.IsNull( directory.FindPackage( "" ) );
		Assert.IsNull( directory.FindPackage( "not-a-valid-ident" ) );
	}

	//The path-based lookup used to resolve Asset.Package fast-rejects any absolute path that is
	//not inside the cloud folder, returning null without touching the database or filesystem.
	[TestMethod]
	public void FindPackageByPath_OutsideCloudFolder_ReturnsNull()
	{
		var path = DbPath( "by_path" );
		using var directory = new CloudAssetDirectory( path );

		Assert.IsNull( directory.FindPackage( "C:/projects/mygame/models/thing.vmdl_c", "models/thing.vmdl_c" ) );
	}
}
