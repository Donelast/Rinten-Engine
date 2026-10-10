using Rinten;
using System;
using System.IO;

namespace Editor;

/// <summary>A picture the editor draws with.</summary>
public partial class Pixmap : IValid
{
	/// <summary>The picture itself, when this is one of ours.</summary>
	public Bitmap picture;

	/// <summary>Whether there is still a picture behind this.</summary>
	public bool IsValid => picture.IsValid();

	/// <summary>Frees the GPU texture of a replaced pixmap; leaking them exhausts device descriptors</summary>
	public void Release()
	{
		picture?.Dispose();
		picture = null;
	}

	//One that is only a picture, with nothing native behind it.
	private Pixmap()
	{
	}

	/// <summary>A new picture of this size, with nothing in it.</summary>
	public Pixmap( int width, int height )
	{
		picture = new Bitmap( Math.Max( width, 1 ), Math.Max( height, 1 ) );

		Clear( Color.Transparent );
	}

	public Pixmap( Vector2 size ) : this( size.x.CeilToInt(), size.y.CeilToInt() )
	{
	}

	internal bool OnPanels => EditorBackend.Panels;

	//Where the editor's own images live, for a name with no path on it.
	private static readonly string[] Folders =
	{
		"addons/tools/assets",
	};

	//A picture read off the disk, or null where there is none by that name.
	private static Pixmap FromLoadedFile( string filename )
	{
		Bitmap loaded = null;

		try
		{
			loaded = Load( filename );

			if ( loaded is null ) return null;

			//The pixmap owns the bitmap from here, so it is not ours to let go of when we leave.
			var made = new Pixmap { picture = loaded };

			loaded = null;

			return made;
		}
		finally
		{
			loaded?.Dispose();
		}
	}

	private static Bitmap Load( string filename )
	{
		if ( string.IsNullOrEmpty( filename ) ) return null;

		var colon = filename.IndexOf( ':' );

		if ( colon > 1 ) filename = filename[(colon + 1)..];

		var root = Environment.CurrentDirectory;

		foreach ( var folder in Folders )
		{
			var path = Path.Combine( root, folder, filename );

			if ( !File.Exists( path ) ) continue;

			try
			{
				return Bitmap.CreateFromBytes( File.ReadAllBytes( path ) );
			}
			catch ( Exception )
			{
				return null;
			}
		}

		return File.Exists( filename ) ? Bitmap.CreateFromBytes( File.ReadAllBytes( filename ) ) : null;
	}

	//Writes the picture out as a file. Both formats come from the bitmap itself, which is what holds the pixels.
	internal bool SaveManaged( string filename, bool jpeg, int quality )
	{
		if ( picture is null || string.IsNullOrWhiteSpace( filename ) ) return false;

		try
		{
			var bytes = jpeg ? picture.ToJpg( quality ) : picture.ToPng();

			if ( bytes is null ) return false;

			System.IO.File.WriteAllBytes( filename, bytes );

			return true;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't save {filename}: {e.Message}" );

			return false;
		}
	}
}
