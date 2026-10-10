using System;
using System.IO;
using System.Text;
using Android.Content;
using Android.OS;
using Android.Provider;

//System has a Uri of its own, and what a content resolver takes is Android's.
using AndroidUri = global::Android.Net.Uri;

namespace Rinten.Android;

//Writes text files to Downloads/Rinten via the media store; skipped below Android 10
internal static class Downloads
{
	//The folder among the player's downloads.
	private static string Folder => Path.Combine( global::Android.OS.Environment.DirectoryDownloads, "Rinten" );

	//Whether this phone has a Downloads to write to.
	public static bool Here => Build.VERSION.SdkInt >= BuildVersionCodes.Q;

	//A new file, or null where there is none to be had. The store numbers a name that is taken - name (1).txt - rather than write over it.
	public static AndroidUri Create( Context context, string name )
	{
		if ( !Here || context?.ContentResolver is not { } resolver ) return null;

		try
		{
			using var values = new ContentValues();

			values.Put( MediaStore.IMediaColumns.DisplayName, name );
			values.Put( MediaStore.IMediaColumns.MimeType, "text/plain" );
			values.Put( MediaStore.IMediaColumns.RelativePath, Folder );

			return resolver.Insert( MediaStore.Downloads.ExternalContentUri, values );
		}
		catch ( Exception e )
		{
			global::Android.Util.Log.Error( "rinten", $"no file in Downloads: {e.Message}" );
			return null;
		}
	}

	//The whole of a text into a file made by Create, over what was there. False where the file is gone or no longer
	//this app's to write - one made by an earlier install of it, which the store keeps but will not hand back.
	public static bool Put( Context context, AndroidUri file, string text )
	{
		if ( file is null || context?.ContentResolver is not { } resolver ) return false;

		try
		{
			using var stream = resolver.OpenOutputStream( file, "rwt" );

			if ( stream is null ) return false;

			var bytes = Encoding.UTF8.GetBytes( text ?? "" );

			stream.Write( bytes, 0, bytes.Length );
			stream.Flush();

			return true;
		}
		catch ( Exception e )
		{
			global::Android.Util.Log.Error( "rinten", $"could not write {file}: {e.Message}" );
			return false;
		}
	}

	//A new file with the text in it, and where it is as a player would say it - or null.
	public static string Write( Context context, string name, string text )
	{
		using var file = Create( context, name );

		return Put( context, file, text ) ? Path.Combine( Folder, name ) : null;
	}
}
