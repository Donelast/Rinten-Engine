using System;
using System.IO;
using System.Linq;

using Rinten.CoreAnimation;

namespace Rinten;

//Clips loaded by the core, one shared copy each, kept for the process; only loads existing .anim_c
internal static class CoreClips
{
	private static readonly Logger log = new( "Core" );

	private static readonly object padlock = new();
	private static readonly Dictionary<string, AnimClipData> clips = new( StringComparer.OrdinalIgnoreCase );

	//Nothing here asks whether the native library loaded, and it used to. That probe is Box3D answering its version; reading a clip is managed from the file up, and the two have nothing to do with each other.
	/// <summary>The clip a resource path names, or null if there is no such clip.</summary>
	public static AnimClipData Clip( string clipPath )
	{
		if ( string.IsNullOrWhiteSpace( clipPath ) ) return null;

		var path = clipPath.Replace( '\\', '/' );

		lock ( padlock )
		{
			if ( clips.TryGetValue( path, out var cached ) && cached is not null ) return cached;

			var clip = Load( path );
			if ( clip is not null )
				clips[path] = clip;
			else
				clips.Remove( path );

			return clip;
		}
	}

	/// <summary>Drop a cached clip so the next load reads the file that was just compiled.</summary>
	public static void Forget( string clipPath )
	{
		if ( string.IsNullOrWhiteSpace( clipPath ) ) return;

		var path = clipPath.Replace( '\\', '/' );
		var compiled = Path.ChangeExtension( path, ".anim_c" );
		var name = Path.GetFileNameWithoutExtension( path );

		lock ( padlock )
		{
			clips.Remove( path );
			clips.Remove( compiled );

			if ( string.IsNullOrWhiteSpace( name ) ) return;

			foreach ( var key in clips.Keys.ToList() )
			{
				if ( Path.GetFileNameWithoutExtension( key ).Equals( name, StringComparison.OrdinalIgnoreCase ) )
					clips.Remove( key );
			}
		}
	}

	private static AnimClipData Load( string clipPath )
	{
		var compiledPath = Path.ChangeExtension( clipPath, ".anim_c" );
		var compiled = CoreModel.ResolveOnDisk( compiledPath );

		if ( compiled is null ) return null;

		var clip = AnimClipData.Load( compiled );
		if ( clip is null ) log.Warning( $"{compiledPath} is not a clip this core understands" );

		return clip;
	}

	/// <summary>A clip's name, for anything that only has its path.</summary>
	public static string SequenceName( string clipPath )
		=> Path.GetFileNameWithoutExtension( clipPath );
}
