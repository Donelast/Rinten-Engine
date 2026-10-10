using System.IO;
using System.Text.Json.Nodes;

namespace Rinten.Audio;

//Loaded sounds, one copy per file shared by every playing; see CoreSound
internal static class CoreSounds
{
	private static readonly Logger log = new( "Audio" );

	private static readonly object padlock = new();
	private static readonly Dictionary<string, CoreSound> sounds = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The sound at a path, or null if we have none for it.</summary>
	public static CoreSound Get( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) || !CoreNative.Available ) return null;

		var wanted = Ours( path );

		lock ( padlock )
		{
			if ( sounds.TryGetValue( wanted, out var cached ) ) return cached;

			var sound = Load( wanted );
			sounds[wanted] = sound;

			return sound;
		}
	}

	/// <summary>Let go of the sound loaded from one file, so the next ask reads it again.</summary>
	public static void Forget( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return;

		var wanted = Ours( path );

		lock ( padlock )
		{
			if ( !sounds.Remove( wanted ) ) return;

			looped.Remove( wanted );
		}
	}

	//The name this sound is known by, whatever the game called it. A game names the audio file itself - music/menu.wav - and the old pipeline compiles
	//that into a .vsnd beside it. So what a sound is cached under is the file, and a .vsnd asked for means the file it was made from.
	private static string Ours( string path )
	{
		var wanted = path.Replace( '\\', '/' );

		return wanted.EndsWith( ".vsnd", StringComparison.OrdinalIgnoreCase )
			? wanted[..^5]
			: wanted;
	}

	//The audio formats we read.
	private static readonly string[] Formats = { ".wav", ".mp3", ".flac", ".ogg" };

	private static CoreSound Load( string path )
	{
		//The file itself, which is what a game names - or what the editor baked out of it, where it was given settings to bake.
		if ( Formats.Any( x => path.EndsWith( x, StringComparison.OrdinalIgnoreCase ) ) )
		{
			var audio = Baked( path ) ?? CoreModel.ResolveOnDisk( path );

			return audio is null ? null : AsDeclared( path, CoreSound.Load( audio ) );
		}

		//A sound of ours: a file plus what to do with it. Asked for by name, or sitting beside a compiled sound that was asked for without one.
		var definition = CoreModel.ResolveTextOnDisk( path.EndsWith( ".snd", StringComparison.OrdinalIgnoreCase )
			? path
			: path + ".snd" );

		if ( definition is null )
		{
			//A name with no extension is whichever file a .vsnd would have been compiled from.
			foreach ( var format in Formats )
			{
				var beside = CoreModel.ResolveOnDisk( path + format );
				if ( beside is not null ) return AsDeclared( path, CoreSound.Load( beside ) );
			}

			return null;
		}

		string source;
		bool looping;
		float loopStart;
		float loopEnd;

		try
		{
			var json = JsonNode.Parse( CompiledFile.TextOf( definition ) );

			source = json?["Source"]?.GetValue<string>() ?? "";
			looping = json?["Looping"]?.GetValue<bool>() ?? false;
			loopStart = json?["LoopStart"]?.GetValue<float>() ?? 0.0f;
			loopEnd = json?["LoopEnd"]?.GetValue<float>() ?? 0.0f;
		}
		catch ( Exception e )
		{
			log.Warning( $"{path}: {e.Message}" );
			return null;
		}

		if ( string.IsNullOrWhiteSpace( source ) )
		{
			log.Warning( $"{path}: names no audio file" );
			return null;
		}

		//Beside the definition first, which is where an exported file usually sits; then as a project path, which is what a picker writes.
		var next = Path.Combine( Path.GetDirectoryName( definition ) ?? "", source ).Replace( '\\', '/' );

		var file = File.Exists( next ) ? next : CoreModel.ResolveOnDisk( source );

		if ( file is null )
		{
			log.Warning( $"{path}: names '{source}', which is not in the project" );
			return null;
		}

		if ( looping ) looped.Add( path );
		else looped.Remove( path );

		//Quiet when a file will not read: whatever asked falls back to the compiled sound, which is what that fallback is there for.
		var sound = CoreSound.Load( file );

		if ( sound is not null && looping )
		{
			//The definition's region, in seconds, where it gives one; what the file marks where it does not; the whole sound otherwise.
			if ( loopStart > 0.0f || loopEnd > 0.0f )
			{
				sound.SetLoop( (long)( loopStart * sound.SampleRate ), (long)( loopEnd * sound.SampleRate ) );
			}
			else if ( sound.DeclaredLoopStart >= 0 )
			{
				sound.SetLoop( sound.DeclaredLoopStart, sound.DeclaredLoopEnd );
			}
		}

		return sound;
	}

	//The editor's baked wav beside the source (name_c), or null if missing or older than the source
	private static string Baked( string path )
	{
		var baked = CoreModel.ResolveOnDisk( CompiledPaths.Built( path ) );
		if ( baked is null ) return null;

		var source = CoreModel.ResolveOnDisk( path );

		try
		{
			if ( source is not null && File.GetLastWriteTimeUtc( baked ) < File.GetLastWriteTimeUtc( source ) )
				return null;
		}
		catch ( IOException )
		{
			return null;
		}

		return baked;
	}

	//An audio file asked for by itself, with nothing said about it: it loops if the file
	//marks a loop, between the marks - the way sound editors say a sound is meant to go round.
	private static CoreSound AsDeclared( string path, CoreSound sound )
	{
		if ( sound is null ) return null;

		if ( sound.DeclaredLoopStart >= 0 )
		{
			sound.SetLoop( sound.DeclaredLoopStart, sound.DeclaredLoopEnd );
			looped.Add( path );
		}
		else
		{
			looped.Remove( path );
		}

		return sound;
	}

	/// <summary>Takes a sound the game built at runtime - a wav, an mp3, an ogg, in memory - and holds it under a name of its own, so it plays like any other. Null if it could not be read.</summary>
	public static CoreSound FromMemory( string name, ReadOnlySpan<byte> data, bool looping, long loopStart = 0, long loopEnd = 0 )
	{
		if ( string.IsNullOrWhiteSpace( name ) || data.Length == 0 || !CoreNative.Available ) return null;

		var sound = CoreSound.FromMemory( data );
		if ( sound is null ) return null;

		sound.SetLoop( loopStart, loopEnd );

		var wanted = Ours( name );

		lock ( padlock )
		{
			sounds[wanted] = sound;
		}

		if ( looping ) looped.Add( wanted );
		else looped.Remove( wanted );

		return sound;
	}

	//Which sounds play round and round, by what their definition said.
	private static readonly HashSet<string> looped = new( StringComparer.OrdinalIgnoreCase );

	public static bool IsLooping( string path ) => looped.Contains( Ours( path ?? "" ) );

	/// <summary>A sampler for one playing of a sound, or null if this is not one of ours.</summary>
	public static CoreAudioSampler Sampler( string path )
	{
		var sound = Get( path );
		if ( sound is null ) return null;

		var sampler = new CoreAudioSampler( sound, IsLooping( path ) );

		return sampler.IsValid ? sampler : null;
	}
}
