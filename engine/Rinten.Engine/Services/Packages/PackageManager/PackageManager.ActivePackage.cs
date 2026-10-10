using System.IO;
using System.Net;
using System.Threading;

namespace Rinten;

internal static partial class PackageManager
{
	/// <summary>Describes a package that is currently mounted. Mounted packages are shared between client, server and editor. We keep track of which host is using which package using Tags.</summary>
	public class ActivePackage
	{
		public Package Package { get; private set; }
		public BaseFileSystem FileSystem { get; private set; }

		public PackageFileSystem PackageFileSystem { get; private set; }

		public BaseFileSystem AssemblyFileSystem { get; internal set; }

		/// <summary>The project settings folder</summary>
		public BaseFileSystem ProjectSettings { get; private set; }

		/// <summary>The project's localization folder</summary>
		public BaseFileSystem Localization { get; private set; }

		public HashSet<string> Tags { get; } = new( StringComparer.OrdinalIgnoreCase );

		internal static async Task<ActivePackage> Create( Package package, CancellationToken token, PackageLoadOptions options )
		{
			var o = new ActivePackage();
			o.Package = package;

			if ( package is LocalPackage localPackage )
			{
				var projectSettingsPath = System.IO.Path.Combine( localPackage.Project.GetRootPath(), "ProjectSettings" );

				o.ProjectSettings = new AggregateFileSystem();
				if ( System.IO.Directory.Exists( projectSettingsPath ) )
				{
					o.ProjectSettings.CreateAndMount( projectSettingsPath );
				}

				o.Localization ??= new AggregateFileSystem();
				if ( System.IO.Directory.Exists( localPackage.LocalizationPath ) )
				{
					o.Localization.CreateAndMount( localPackage.LocalizationPath );
				}

				o.FileSystem = new AggregateFileSystem();

				if ( System.IO.Directory.Exists( localPackage.CodePath ) )
				{
					if ( localPackage.CodePath != null )
					{
						o.FileSystem.CreateAndMount( localPackage.CodePath );
					}
				}

				if ( System.IO.Directory.Exists( localPackage.ContentPath ) )
				{
					o.FileSystem.CreateAndMount( localPackage.ContentPath );
				}

				o.AssemblyFileSystem = new AggregateFileSystem();

				if ( Application.IsStandalone )
				{
					var binPath = Path.Combine( localPackage.Project.GetRootPath(), ".bin" );
					System.IO.Directory.CreateDirectory( binPath );
					o.AssemblyFileSystem.CreateAndMount( binPath );
				}
				else
				{
					o.AssemblyFileSystem.Mount( localPackage.AssemblyFileSystem );
				}

			}
			else
			{
				await o.DownloadAsync( token, options );
			}

			ActivePackages.Add( o );

			o.Mount( options.ReloadResources );

			return o;
		}

		public void AddContextTag( string tag )
		{
			Tags.Add( tag );

			//this tag just became active
			OnPackageInstalledToContext?.Invoke( this, tag );
		}

		public void RemoveContextTag( string tag )
		{
			Tags.Remove( tag );
		}

		//Set the filesystem up from this downloaded asset
		private async Task DownloadAsync( CancellationToken token, PackageLoadOptions options )
		{
			Assert.True( Package.IsRemote );

			PackageFileSystem = await Package.Download( token, options );

			if ( PackageFileSystem is null )
			{
				throw new WebException( $"Unable to download package '{Package.FullIdent}'" );
			}

			//Mount downloaded filesystem as our main filesystem
			FileSystem = new AggregateFileSystem();
			FileSystem.Mount( PackageFileSystem );

			//Mount localization data from this package
			Localization ??= new AggregateFileSystem();
			if ( FileSystem.DirectoryExists( "localization" ) )
			{
				//Mount as a subsystem of the package's FileSystem
				Localization.Mount( FileSystem.CreateSubSystem( "localization" ) );
			}

			//If the ProjectSettings folder exists, we can create a filesystem for it. If not,
			//just create a memory filesystem, which will be empty, but at least won't be null.
			if ( FileSystem.DirectoryExists( "ProjectSettings" ) )
			{
				ProjectSettings = FileSystem.CreateSubSystem( "ProjectSettings" );
			}
			else
			{
				ProjectSettings = new MemoryFileSystem();
			}

			//Mount assembly from this package
			AssemblyFileSystem ??= new AggregateFileSystem();
			if ( FileSystem.DirectoryExists( ".bin" ) )
			{
				//Mount as a subsystem of the package's FileSystem
				AssemblyFileSystem.Mount( FileSystem.CreateSubSystem( ".bin" ) );
			}

			var dllFs = await DownloadBinDllsAsync( Package.Revision, token );
			if ( dllFs != null )
			{
				AssemblyFileSystem ??= new AggregateFileSystem();
				AssemblyFileSystem.Mount( dllFs );
			}
		}

		private static async Task<MemoryFileSystem> DownloadBinDllsAsync( Package.IRevision revision, CancellationToken token )
		{
			var files = revision?.Manifest?.Files;
			if ( files is null ) return null;

			var dllFiles = files
				.Where( f => f.Path.StartsWith( ".bin/", StringComparison.OrdinalIgnoreCase ) &&
							 f.Path.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) )
				.ToArray();

			if ( dllFiles.Length == 0 ) return null;

			var memFs = new MemoryFileSystem();

			foreach ( var file in dllFiles )
			{
				token.ThrowIfCancellationRequested();
				LoadingScreen.Subtitle = System.IO.Path.GetFileName( file.Path );

				var bytes = await Rinten.Utility.Web.GrabFile( file.Url, token );
				if ( bytes is not null )
				{
					memFs.WriteAllBytes( System.IO.Path.GetFileName( file.Path ), bytes );
				}
			}

			LoadingScreen.Subtitle = null;
			return memFs;
		}

		internal bool HasPrecompiledDlls()
		{
			return AssemblyFileSystem?.FindFile( "/", "*.dll", true ).Any() ?? false;
		}

		internal bool HasManifestDlls()
		{
			var files = Package.Revision?.Manifest?.Files;
			return files?.Any( f => f.Path.StartsWith( ".bin/", StringComparison.OrdinalIgnoreCase ) && f.Path.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) ) ?? false;
		}

		internal bool HasCodeArchives()
		{
			return FileSystem.FindFile( "/", "*.cll", true ).Any();
		}

		private void Mount( bool reloadResources = true )
		{
			MountedFileSystem.Mount( FileSystem );

			//Resources load on demand from the filesystem that was just mounted, so nothing has to be told to look again.

			//Rinten.FileSystem.Mounted.Mount( FileSystem );

			//this only makes sense if the package is a local package
			//Engine.SearchPath.Add( AbsolutePath, "GAME", true );
		}

		/// <summary>Called to unmount and remove this package from being active</summary>
		public void Delete()
		{
			MountedFileSystem.UnMount( FileSystem );

			//Make sure we unmount the package from the global filesystem, so that any other packages that might have been mounted on top of it don't get broken.
			Rinten.FileSystem.Mounted?.UnMount( FileSystem );

			FileSystem.Dispose();
			FileSystem = default;

			PackageFileSystem?.Dispose();
			PackageFileSystem = null;

			AssemblyFileSystem.Dispose();
			AssemblyFileSystem = null;

			//Reload any resident resources that were just unmounted (they shouldn't be used & will appear as an error, or a local variant)
		}

	}
}
