using System;
using System.IO;

namespace Editor;

/// <summary>
/// FFmpeg, in an export: <c>bin/&lt;platform&gt;/ffmpeg</c>, carried only by a game
/// that plays or writes video.
/// </summary>
/// <remarks>
/// Eighty megabytes of codecs that a game without video never opens - and without
/// them a player plays nothing and a writer writes nothing, saying why once (see
/// Rinten.FFmpeg). So the question is whether the game has video in it, which is
/// asked of two things: its code, for a player or a writer it makes itself, and
/// its files, for a video that is only ever a picture on something - a material,
/// a panel's background - and is named by no code at all.
/// <para>
/// The licence goes with the libraries. They are LGPL, and a game that ships them
/// ships that too.
/// </para>
/// </remarks>
partial class StandaloneExporter
{
	//The types whose use means a game has video.
	private static readonly (string Namespace, string Name)[] VideoTypes =
	[
		("Rinten", "VideoPlayer"),
		("Rinten", "VideoWriter"),
	];

	//What a file a game ships is called when it is a video - what a texture of it is played from.
	private static bool IsVideoFile( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return false;

		return Rinten.TextureLoader.VideoTextureLoader.Extensions.Contains( Path.GetExtension( name ) );
	}

	private bool GameUsesVideo()
	{
		if ( GameReferencesAny( VideoTypes ) ) return true;

		return PackageManifest?.Assets?.Any( asset => IsVideoFile( asset.Name ) ) ?? false;
	}

	//FFmpeg's files for the platform being exported for, where the game has video. Empty
	//otherwise, and empty - with the reason said - where they are not there to carry.
	private IEnumerable<string> GetVideoFiles( string engineDir )
	{
		var platform = _exportConfig.Platform ?? ExportPlatform.Host;

		//A phone and a page have no FFmpeg: video there does nothing.
		if ( platform.IsAndroid || platform.IsWeb ) return [];

		if ( !GameUsesVideo() ) return [];

		var folder = Path.Combine( engineDir, platform.BinFolder, "ffmpeg" );
		var codecs = Directory.Exists( folder )
			? Directory.EnumerateFiles( folder ).Any( file => Path.GetFileName( file ).Contains( "avcodec", StringComparison.OrdinalIgnoreCase ) )
			: false;

		if ( !codecs )
		{
			Problem( $"the game has video, but there is no FFmpeg for {platform.Title} in {folder} - " +
				"its videos will play nothing. Fetch it with: " +
				$"dotnet run --project engine/Tools/RintenBuild -- fetch-natives --platform {platform.Id}" );

			return [];
		}

		Logger.Info( "The game has video: FFmpeg goes with it." );

		return Directory.EnumerateFiles( folder, "*", SearchOption.TopDirectoryOnly );
	}
}
