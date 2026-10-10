using System;
using System.IO;
using System.Linq;

namespace Editor;

/// <summary>The animations inside an export, named the way a model calls them - and put on a model as its clips.</summary>
public static class ModelTakes
{
	/// <summary>One take: Name is the file's own spelling for the importer, Title is the readable clip name</summary>
	public readonly record struct Take( int Index, string Name, string Title );

	/// <summary>The animations in an export on disk, in the order the file holds them.</summary>
	public static List<Take> In( string path )
	{
		var takes = new List<Take>();

		if ( string.IsNullOrWhiteSpace( path ) || !File.Exists( path ) ) return takes;

		var bare = Path.GetFileNameWithoutExtension( path );
		var extension = Path.GetExtension( path )?.ToLowerInvariant();

		//glTF names an animation after the animation; an FBX stack is as often named after the scene or the
		//model it came out of. Which is the difference between trusting a lone name and falling back to the file.
		var actionNames = extension is ".gltf" or ".glb";

		List<string> names;

		switch ( extension )
		{
			case ".fbx":
			case ".gltf":
			case ".glb":
				names = ModelImportCatalog.TakeNames( path );
				break;

			//One take and no name for it. Written as the file's own name rather than blank because that is
			//what these clips have always carried, and a definition already saying it should keep matching.
			case ".smd":
			case ".anim_c":
				names = new List<string> { bare };
				break;

			default:
				return takes;
		}

		for ( var i = 0; i < names.Count; i++ )
			takes.Add( new Take( i, names[i] ?? "", Title( names[i], bare, i, names.Count, actionNames ) ) );

		return Apart( takes );
	}

	//For a file somebody picked to be an animation: whatever it is, it is that one animation, and refusing to name it because nothing could be read out of it helps no one.
	/// <summary>The one take a file is, where its takes could not be listed at all.</summary>
	public static Take Whole( string file )
	{
		var bare = Path.GetFileNameWithoutExtension( file ) ?? "";

		return new Take( 0, bare, Title( bare, bare, 0, 1 ) );
	}

	/// <summary>Adds every take the definition does not already list as a clip, and says how many that was.</summary>
	public static int AddClips( ModelDefinition definition, string file, IReadOnlyList<Take> takes, float scale = 1.0f )
	{
		if ( definition is null || string.IsNullOrWhiteSpace( file ) || takes is null )
			return 0;

		var added = 0;

		foreach ( var take in takes )
		{
			if ( Has( definition, file, take ) ) continue;

			AddClip( definition, file, take, scale );

			added++;
		}

		return added;
	}

	/// <summary>Adds one take as a clip even if already listed; AddClips is the every-take-once version</summary>
	public static ModelDefinition.ModelClip AddClip( ModelDefinition definition, string file, Take take, float scale = 1.0f )
	{
		var clip = new ModelDefinition.ModelClip
		{
			File = file,
			TakeName = take.Name,

			//Named and numbered both: the name is what an export the editor listed answers to, and the number is what is left when it
			//does not - a glTF animation the exporter never named is asked for by its place in the file. See CoreGltfImport.ImportClip.
			Take = take.Index,
			Scale = scale > 0.0f ? scale : 1.0f,
		};

		//What the model calls it, where that is not what the export calls it. This is the name in the tree, the name a graph plays, and
		//the name of the .anim_c - so it is the readable one, and the export's own spelling stays in TakeName where the importer needs it.
		if ( !string.Equals( take.Title, take.Name, StringComparison.Ordinal ) )
			clip.Name = take.Title;

		definition.Clips.Add( clip );

		return clip;
	}

	/// <summary>Whether this take of this file is already a clip on the model.</summary>
	public static bool Has( ModelDefinition definition, string file, Take take )
		=> definition.Clips.Any( clip =>
			string.Equals( clip.File, file, StringComparison.OrdinalIgnoreCase )
			&& ( string.IsNullOrWhiteSpace( clip.TakeName )
				? clip.Take == take.Index
				: string.Equals( clip.TakeName, take.Name, StringComparison.OrdinalIgnoreCase ) ) );

	/// <summary>Clip title: the take's own action name when trustworthy, else the file name cut at @</summary>
	public static string Title( string take, string bare, int index, int count, bool actionNames = false )
	{
		var action = Action( take );
		var named = !string.IsNullOrWhiteSpace( action ) && !Boilerplate( action );

		if ( count <= 1 )
			return named && ( actionNames || Spelled( take ) ) ? action : Named( bare );

		return named ? action : $"{Named( bare )}_{index}";
	}

	//Keeps two takes of one file from reading as the same clip.
	static List<Take> Apart( List<Take> takes )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		for ( var i = 0; i < takes.Count; i++ )
		{
			if ( seen.Add( takes[i].Title ) ) continue;

			takes[i] = takes[i] with { Title = $"{takes[i].Title}_{takes[i].Index}" };
			seen.Add( takes[i].Title );
		}

		return takes;
	}

	//Whether the export spelled the action out rather than naming the take after itself.
	static bool Spelled( string take )
		=> take is not null && ( take.Contains( '|' ) || take.Contains( "::", StringComparison.Ordinal ) );

	//The action out of a take name: what is after the last | or :: .
	static string Action( string take )
	{
		if ( string.IsNullOrWhiteSpace( take ) ) return "";

		var name = take.Trim();

		var mark = name.LastIndexOf( "::", StringComparison.Ordinal );
		if ( mark >= 0 ) name = name[( mark + 2 )..];

		var bar = name.LastIndexOf( '|' );
		if ( bar >= 0 ) name = name[( bar + 1 )..];

		return name.Trim();
	}

	//The name out of a file name: what is after the last @ .
	static string Named( string bare )
	{
		if ( string.IsNullOrWhiteSpace( bare ) ) return "animation";

		var at = bare.LastIndexOf( '@' );
		if ( at < 0 || at == bare.Length - 1 ) return bare;

		return bare[( at + 1 )..];
	}

	//Names an exporter writes when nobody named anything.
	static bool Boilerplate( string name )
	{
		var word = name.Trim();

		if ( word.StartsWith( "take", StringComparison.OrdinalIgnoreCase ) )
		{
			//"Take 001", "Take001", "Take" - the default of every exporter there has ever been, and never what the animation is called.
			var rest = word[4..].Trim();
			if ( rest.Length == 0 || rest.All( char.IsAsciiDigit ) ) return true;
		}

		return word.Equals( "scene", StringComparison.OrdinalIgnoreCase )
			|| word.Equals( "default", StringComparison.OrdinalIgnoreCase )
			|| word.Equals( "unnamed", StringComparison.OrdinalIgnoreCase )
			|| word.Equals( "animstack", StringComparison.OrdinalIgnoreCase )
			|| word.Equals( "mixamo.com", StringComparison.OrdinalIgnoreCase );
	}
}
