using System;
using System.IO;

namespace Editor;

//Creates .mat files by matching textures to standard material suffixes ("_color", "_normal" etc)
internal static class MaterialGenerator
{
	//Where a map goes in the material: a property of its own, or one of its named textures.
	internal readonly record struct Slot( string Property, string Texture = null );

	//A group of texture files that share a directory and a base name, keyed by their suffix.. For
	//example awesome_mat_color.png + awesome_mat_normal.png becomes one set named "awesome_mat"
	internal sealed class TextureSet
	{
		public string Directory { get; init; }

		public string BaseName { get; init; }

		public Dictionary<string, Asset> BySuffix { get; init; }
	}

	//Maps a texture file suffix to where the standard material takes it.
	internal static Dictionary<string, Slot> GetSuffixMap()
	{
		var color = new Slot( "ColorMap" );
		var normal = new Slot( "Normal" );
		var emission = new Slot( "Emission" );
		var rough = new Slot( "Textures", "g_tRoughness" );
		var metal = new Slot( "Textures", "g_tMetalness" );
		var occlusion = new Slot( "Textures", "g_tAO" );

		return new Dictionary<string, Slot>( StringComparer.OrdinalIgnoreCase )
		{
			["color"] = color,
			["colour"] = color,
			["albedo"] = color,
			["basecolor"] = color,
			["diff"] = color,
			["diffuse"] = color,
			["normal"] = normal,
			["nrm"] = normal,
			["rough"] = rough,
			["roughness"] = rough,
			["metal"] = metal,
			["metallic"] = metal,
			["metalness"] = metal,
			["ao"] = occlusion,
			["occlusion"] = occlusion,
			["selfillum"] = emission,
			["emission"] = emission,
			["emissive"] = emission,
		};
	}

	//Find every group of suffixed texture files at or below root directory
	internal static List<TextureSet> DiscoverTextureSets( string rootDirectory, IEnumerable<string> suffixes )
	{
		var result = new List<TextureSet>();

		if ( string.IsNullOrWhiteSpace( rootDirectory ) || suffixes is null )
			return result;

		var ordered = suffixes
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderByDescending( x => x.Length )
			.ToArray();

		if ( ordered.Length == 0 )
			return result;

		var root = rootDirectory.NormalizeFilename( false );
		if ( !root.EndsWith( '/' ) )
			root += "/";

		//grouped by directory + base name, case insensitive
		var byKey = new Dictionary<(string Directory, string BaseName), TextureSet>();

		var images = AssetSystem.All
			.Where( IsImage )
			.OrderBy( x => x.AbsolutePath, StringComparer.OrdinalIgnoreCase );

		foreach ( var asset in images )
		{
			var absolute = asset.AbsolutePath.NormalizeFilename( false );
			if ( !absolute.StartsWith( root, StringComparison.Ordinal ) )
				continue;

			//parsed from the un-normalized name so the material keeps the casing the artist used
			if ( !TrySplitSuffix( Path.GetFileNameWithoutExtension( asset.AbsolutePath ), ordered, out var baseName, out var suffix ) )
				continue;

			//the lowercased path is only ever used for grouping - the set keeps the real one, so writing the material works on case-sensitive filesystems too
			var key = (Path.GetDirectoryName( absolute ).NormalizeFilename( false ), baseName.ToLowerInvariant());

			if ( !byKey.TryGetValue( key, out var set ) )
			{
				set = new TextureSet
				{
					Directory = Path.GetDirectoryName( asset.AbsolutePath ).NormalizeFilename( false, false ),
					BaseName = baseName,
					BySuffix = new Dictionary<string, Asset>( StringComparer.OrdinalIgnoreCase ),
				};

				byKey[key] = set;
				result.Add( set );
			}

			set.BySuffix.TryAdd( suffix, asset );
		}

		return result;
	}

	internal static TextureSet FindSetForSlot( List<TextureSet> sets, string slotName )
	{
		if ( sets is null || string.IsNullOrWhiteSpace( slotName ) )
			return null;

		//Without Blender's duplicate suffix, the way the model compiler matches surfaces.
		var name = Rinten.ModelImportCatalog.Bare( slotName.Trim() );
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		//pick whatever shows up first, user must make sure that they don't have any texture duplicates in model's folder or subfolders
		return sets.FirstOrDefault( x => string.Equals( x.BaseName, name, StringComparison.OrdinalIgnoreCase ) );
	}

	//Get the material for a texture set, creating it next to the textures if it doesn't exist yet
	internal static string GetOrCreateMaterial( TextureSet set, Dictionary<string, Slot> suffixMap )
	{
		if ( set is null || suffixMap is null )
			return null;

		var targetPath = Path.Combine( set.Directory, $"{set.BaseName}.mat" );

		//make sure that we never generate a material if one already exists
		if ( !File.Exists( targetPath ) )
		{
			var contents = BuildMaterialText( set, suffixMap );
			if ( contents is null )
				return null;

			try
			{
				File.WriteAllText( targetPath, contents );
			}
			catch ( Exception e )
			{
				Log.Warning( $"MaterialGenerator: couldn't write '{targetPath}' - {e.Message}" );
				return null;
			}
		}

		var asset = AssetSystem.RegisterFile( targetPath );
		if ( asset is null )
		{
			Log.Warning( $"MaterialGenerator: couldn't register '{targetPath}'" );
			return null;
		}

		return asset.Path;
	}

	//A material of ours as it is written on disk - the same shape the asset browser's "Create Material" writes; see MaterialMenu.MaterialFileContent.
	static string BuildMaterialText( TextureSet set, Dictionary<string, Slot> suffixMap )
	{
		var content = new Dictionary<string, object>();
		var textures = new Dictionary<string, string>();

		content["Shader"] = Rinten.MaterialDefinition.StandardShader;

		foreach ( var (suffix, asset) in set.BySuffix.OrderBy( x => x.Key, StringComparer.OrdinalIgnoreCase ) )
		{
			if ( !suffixMap.TryGetValue( suffix, out var slot ) )
				continue;

			if ( slot.Texture is null )
				content.TryAdd( slot.Property, asset.RelativePath );
			else
				textures.TryAdd( slot.Texture, asset.RelativePath );
		}

		if ( textures.Count == 0 && content.Count == 1 )
			return null;

		if ( textures.Count > 0 )
			content["Textures"] = textures;

		//Metalness is a switch as well as a number - it is the map being on - so a metal map comes with it on.
		if ( textures.ContainsKey( "g_tMetalness" ) )
			content["Metalness"] = 1.0f;

		content["__version"] = 2;

		return Rinten.Json.Serialize( content );
	}

	static bool TrySplitSuffix( string stem, string[] orderedSuffixes, out string baseName, out string suffix )
	{
		foreach ( var candidate in orderedSuffixes )
		{
			//needs at least one character of base name before the underscore
			if ( stem.Length <= candidate.Length + 1 )
				continue;

			if ( stem[stem.Length - candidate.Length - 1] != '_' )
				continue;

			if ( !stem.EndsWith( candidate, StringComparison.OrdinalIgnoreCase ) )
				continue;

			baseName = stem.Substring( 0, stem.Length - candidate.Length - 1 );
			suffix = candidate;
			return true;
		}

		baseName = null;
		suffix = null;
		return false;
	}

	static bool IsImage( Asset asset )
	{
		if ( AssetType.ImageFile is null )
			return false;

		return asset?.AbsolutePath is not null && asset.AssetType == AssetType.ImageFile;
	}
}
