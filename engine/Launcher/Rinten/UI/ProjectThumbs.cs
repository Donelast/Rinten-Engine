using System.IO;

namespace Editor;

internal static class ProjectThumbs
{
	static readonly Dictionary<string, Texture> Cache = new();

	public static Texture Get( Project project )
	{
		var path = project?.GetThumbPath();
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			return null;

		var key = path + ":" + File.GetLastWriteTimeUtc( path ).Ticks;

		if ( Cache.TryGetValue( key, out var texture ) )
			return texture;

		try
		{
			using var bitmap = Bitmap.CreateFromBytes( File.ReadAllBytes( path ) );
			if ( bitmap is null || !bitmap.IsValid )
				return null;

			texture = bitmap.ToTexture( mips: false );
			Cache[key] = texture;
			return texture;
		}
		catch ( Exception )
		{
			return null;
		}
	}
}
