using System;
using System.IO;

namespace Editor.MdlEditor;

//Finds which model a graph belongs to and a model's definition, read off the assets each time
internal static class ModelGraphResolver
{
	/// <summary>The model definition a graph is built for, or null where its model has none on disk.</summary>
	public static Asset ModelFor( Data graph ) => ModelFor( graph?.SelectedVariation?.TargetModelPath );

	/// <summary>The <c>.mdl</c> beside a model path - a compiled <c>.vmdl</c> or the definition itself.</summary>
	public static Asset ModelFor( string modelPath )
	{
		if ( string.IsNullOrWhiteSpace( modelPath ) ) return null;

		var path = modelPath.Replace( '\\', '/' );

		if ( !path.EndsWith( ".mdl", StringComparison.OrdinalIgnoreCase ) )
			path = Path.ChangeExtension( path, ".mdl" );

		return AssetAt( path );
	}

	/// <summary>A model asset's definition - the one the model editor has open, where it has, and the asset system's otherwise. The same object either way; asked in this order so the one being edited is never second-guessed.</summary>
	public static ModelDefinition DefinitionAt( Asset model )
	{
		if ( model is null ) return null;

		if ( OpenModels.Holding( model.Path ) is { ModelDefinition: { } open } )
			return open;

		return model.TryLoadResource<ModelDefinition>( out var loaded ) ? loaded : null;
	}

	/// <summary>The definition of a compiled model, from an open editor first and the disk after it.</summary>
	public static ModelDefinition DefinitionOf( Model model )
	{
		var path = model is { IsValid: true } ? model.ResourcePath ?? model.Name : null;
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		var mdl = Path.ChangeExtension( path.Replace( '\\', '/' ), ".mdl" );

		if ( OpenModels.Holding( mdl ) is { ModelDefinition: { } open } )
			return open;

		return AssetSystem.FindByPath( mdl )?.TryLoadResource<ModelDefinition>( out var found ) == true ? found : null;
	}

	/// <summary>The asset at a path the project names, or null where there is no such file.</summary>
	public static Asset AssetAt( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		if ( AssetSystem.FindByPath( path ) is { HasSourceFile: true } found )
			return found;

		var root = Project.Current?.GetAssetsPath();
		if ( string.IsNullOrWhiteSpace( root ) )
			return null;

		var absolute = Path.Combine( root, path.Replace( '/', Path.DirectorySeparatorChar ) );

		return File.Exists( absolute ) ? AssetSystem.RegisterFile( absolute ) : null;
	}

	/// <summary>Whether two project paths name the same file, whichever way their slashes lean.</summary>
	public static bool SamePath( string a, string b )
	{
		if ( string.IsNullOrWhiteSpace( a ) || string.IsNullOrWhiteSpace( b ) ) return false;

		return string.Equals( a.Replace( '\\', '/' ), b.Replace( '\\', '/' ), StringComparison.OrdinalIgnoreCase );
	}

	public static bool IsModelExtension( string extension )
		=> string.Equals( extension, "mdl", StringComparison.OrdinalIgnoreCase );
}
