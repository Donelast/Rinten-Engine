using Rinten.Utility;
namespace Editor;

public static partial class EditorUtility
{
	//Anything Bitmap.CreateFromBytes decodes - png, jpeg, webp, tga, psd, tif, ies. A path on disk, or one in the project's files. Null where there is no such file or it is not a picture.
	/// <summary>Load a float bitmap. This is usually a png, tga, exr, psd</summary>
	public static FloatBitmap LoadBitmap( string filename )
	{
		if ( string.IsNullOrWhiteSpace( filename ) ) return null;

		byte[] bytes = null;

		if ( System.IO.File.Exists( filename ) )
			bytes = System.IO.File.ReadAllBytes( filename );
		else if ( FileSystem.Mounted?.FileExists( filename ) == true )
			bytes = FileSystem.Mounted.ReadAllBytes( filename ).ToArray();

		if ( bytes is null ) return null;

		using var bitmap = Bitmap.CreateFromBytes( bytes );
		return bitmap is null ? null : FloatBitmap.From( bitmap );
	}
}
