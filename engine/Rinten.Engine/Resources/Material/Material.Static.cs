using NativeEngine;
using System.IO;

namespace Rinten;

/// <summary>A material. Uses several <see cref="Texture"/>s and a shader with specific settings for more interesting visual effects.</summary>
public sealed partial class Material : Resource
{
	/// <summary>Create a new empty material at runtime.</summary>
	public static Material Create( string materialName, string shader, bool anonymous = true )
	{
		//No handle to request from a material system here. A material of ours is the values
		//themselves - a shader and what is set on it - so there is nobody to ask: making one is making one.
		return new Material( new CoreMaterial( materialName, shader ), anonymous );
	}

	static Dictionary<string, Material> shaderMaterials = new Dictionary<string, Material>( StringComparer.OrdinalIgnoreCase );

	/// <summary>Get an empty material based on the specified shader. This will cache the material so that subsequent calls will return the same material.</summary>
	public static Material FromShader( string path )
	{
		var pathSpan = path.AsSpan();
		var shaderDirSpan = Path.GetDirectoryName( pathSpan );
		var shaderNameSpan = Path.GetFileNameWithoutExtension( pathSpan );
		var shaderPath = Path.Join( shaderDirSpan, shaderNameSpan ).NormalizeFilename( false, true, '_' );
		if ( shaderMaterials.TryGetValue( shaderPath, out var material ) )
			return material;

		var materialName = $"__shader_{shaderPath}.vmat";

		material = Create( materialName, path );
		shaderMaterials[shaderPath] = material;
		return material;
	}

	//Dispose all cached shader materials created via FromShader. Called during
	//shutdown to release native strong handles before the resource system tears down.
	internal static void Shutdown()
	{
		foreach ( var material in shaderMaterials.Values )
		{
			material?.Destroy();
		}

		shaderMaterials.Clear();
	}

}
