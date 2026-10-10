using System;

namespace Editor;

public static partial class EditorUtility
{
	private static readonly Logger modelLog = new( "CreateModel" );

	//Said once, rather than once per mesh somebody selected.
	private static bool saidNotYet;

	private static Asset NotYet( string what )
	{
		if ( !saidNotYet )
		{
			saidNotYet = true;

			modelLog.Warning( $"Cannot {what}: writing model files is the importer's job, and it is not written yet. " +
				"Meshes load straight from their own files in the meantime." );
		}

		return null;
	}

	/// <summary>Make a model file out of a mesh file.</summary>
	public static Asset CreateModelFromMeshFile( Asset meshFile, string targetAbsolutePath = null,
		Action<ModelDefinition> configure = null )
	{
		if ( meshFile is null ) return null;

		var source = meshFile.RelativePath;

		if ( string.IsNullOrWhiteSpace( source ) )
		{
			modelLog.Warning( $"{meshFile.AbsolutePath} is not in the project, so a model here could not name it." );
			return null;
		}

		targetAbsolutePath = string.IsNullOrWhiteSpace( targetAbsolutePath )
			? System.IO.Path.ChangeExtension( meshFile.AbsolutePath, ".mdl" )
			: targetAbsolutePath;

		//Never adds to an existing model, or its parts and clips would be duplicated
		if ( System.IO.File.Exists( System.IO.Path.ChangeExtension( targetAbsolutePath, ".mdl" ) ) )
		{
			modelLog.Warning( $"{System.IO.Path.GetFileName( targetAbsolutePath )} is already a model - re-exporting {System.IO.Path.GetFileName( source )} rebuilds it, there is nothing to create" );
			return AssetSystem.FindByPath( System.IO.Path.ChangeExtension( targetAbsolutePath, ".mdl" ) );
		}

		var asset = AssetSystem.CreateResource( "mdl", targetAbsolutePath );

		if ( asset is null )
		{
			modelLog.Warning( $"Could not make a model at {targetAbsolutePath}" );
			return null;
		}

		if ( !asset.TryLoadResource<ModelDefinition>( out var definition ) )
		{
			modelLog.Warning( $"{asset.Path} was made but does not read as a model" );
			return null;
		}

		definition.Meshes.Add( new ModelDefinition.MeshPart
		{
			Name = System.IO.Path.GetFileNameWithoutExtension( source ),
			File = source,
		} );

		configure?.Invoke( definition );

		//Written and compiled: SaveToDisk builds the .mdl_c beside it unless told not to - see Asset.SaveToDisk.
		asset.SaveToDisk( definition );

		return asset;
	}

	public static Asset CreateModelFromPolygonMeshes( PolygonMesh[] polygonMeshes, string targetAbsolutePath )
		=> NotYet( "make a model from polygon meshes" );

	public static Asset CreateModelFromMeshComponents( MeshComponent[] meshComponents, string targetAbsolutePath )
		=> NotYet( "make a model from mesh components" );
}
