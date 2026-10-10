using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.MdlEditor;

//What an FBX beside a model definition has in it - the mesh names, the material names, and where the file actually is on disk. Its own file because it
//is not part of the outliner: six other places ask it the same questions, and it was only ever in there because the outliner was the first of them.
static class ModelSourceCatalog
{
	public static IReadOnlyList<string> Meshes( Asset asset, ModelDefinition.MeshPart part )
	{
		if ( part is null || string.IsNullOrWhiteSpace( part.File ) )
			return Array.Empty<string>();

		var path = Resolve( asset, part.File );
		return string.IsNullOrWhiteSpace( path ) ? Array.Empty<string>() : ModelImportCatalog.MeshNames( path, part.Scale );
	}

	public static IReadOnlyList<string> Materials( Asset asset, ModelDefinition definition )
	{
		var names = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		void Add( string name )
		{
			name = ModelImportCatalog.Bare( name );
			if ( string.IsNullOrWhiteSpace( name ) || !seen.Add( name ) )
				return;

			names.Add( name );
		}

		if ( definition is not null )
		{
			foreach ( var part in definition.AllMeshes() )
			{
				var path = Resolve( asset, part.File );
				if ( string.IsNullOrWhiteSpace( path ) )
					continue;

				foreach ( var name in ModelImportCatalog.MaterialNames( path, part.Scale ) )
					Add( name );
			}

			foreach ( var remap in definition.MaterialRemaps ?? [] )
				Add( remap?.Surface );
		}

		return names;
	}

	public static IReadOnlyList<string> Bones( Asset asset, ModelDefinition definition )
	{
		var names = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		if ( definition is null )
			return names;

		foreach ( var part in definition.AllMeshes() )
		{
			var path = Resolve( asset, part.File );
			if ( string.IsNullOrWhiteSpace( path ) )
				continue;

			foreach ( var name in ModelImportCatalog.BoneNames( path, part.Scale ) )
			{
				if ( string.IsNullOrWhiteSpace( name ) || !seen.Add( name ) )
					continue;

				names.Add( name );
			}
		}

		return names;
	}

	//Found where the definition would find the file, then listed and named by ModelTakes - the same answer "Create Model" on a mesh in the asset browser gets, so a file makes the same clips from either side.
	/// <summary>The animations in an export, in the order the file holds them.</summary>
	public static List<ModelTakes.Take> Takes( Asset asset, string file )
		=> ModelTakes.In( Resolve( asset, file ) );

	public static ModelTakes.Take Whole( string file ) => ModelTakes.Whole( file );

	//A path is typed a character at a time, so a field being edited spends most of its life naming nothing. This is how a listener tells the moment the path became a file from the twenty before it.
	/// <summary>Whether a file a definition names is one that is actually there.</summary>
	public static bool Exists( Asset asset, string file )
		=> !string.IsNullOrWhiteSpace( Resolve( asset, file ) );

	static string Resolve( Asset asset, string file )
	{
		if ( string.IsNullOrWhiteSpace( file ) ) return null;
		if ( File.Exists( file ) ) return file;

		var root = Project.Current?.GetAssetsPath();
		if ( !string.IsNullOrWhiteSpace( root ) )
		{
			var combined = Path.Combine( root, file.Replace( '/', Path.DirectorySeparatorChar ) );
			if ( File.Exists( combined ) ) return combined;
		}

		if ( asset is not null )
		{
			var beside = Path.Combine( Path.GetDirectoryName( asset.GetSourceFile() ?? asset.AbsolutePath ) ?? "",
				Path.GetFileName( file ) );
			if ( File.Exists( beside ) ) return beside;
		}

		return null;
	}
}
