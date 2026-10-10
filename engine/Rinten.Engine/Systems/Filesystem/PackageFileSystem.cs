namespace Rinten;

//A filesystem that redirects a package's local paths to the actual files in a download cache
internal class PackageFileSystem : BaseFileSystem
{
	internal Rinten.Internal.RedirectFileSystem Redirect => system as Rinten.Internal.RedirectFileSystem;

	internal PackageFileSystem()
	{
		//Files are all lowercase in manifests, so ignore case in comparer
		system = AssetDownloadCache.CreateRedirectFileSystem();
	}
}
