using System.Threading.Tasks;

namespace Rinten
{
	//Client class for handling storing and access of downloaded files.
	internal static class DownloadedAssets
	{
		public static BaseFileSystem FileSystem { get; private set; }

		public static void Init()
		{
			Game.AssertClient();

			FileSystem?.Dispose();
			FileSystem = new MemoryFileSystem();
		}

		public static void Shutdown()
		{
			Game.AssertClient();

			FileSystem?.Dispose();
			FileSystem = null;
		}

		//Copy the file from the cache folder to our memory based filesystem. We should probably just be redirecting filenames here.
		internal static async Task AddFileAsync( string filename, string cacheName )
		{
			FileSystem.CreateDirectory( System.IO.Path.GetDirectoryName( filename ) );

			using ( var stream = FileSystem.OpenWrite( filename ) )
			{
				using ( var reader = EngineFileSystem.DownloadedFiles.OpenRead( cacheName ) )
				{
					await reader.CopyToAsync( stream );
				}
			}
		}

		//Get the cache filename we should use for this downloaded file
		internal static string GetCacheName( string filename, ulong crc ) => $"/.sv/{crc:x}.cache";
	}
}
