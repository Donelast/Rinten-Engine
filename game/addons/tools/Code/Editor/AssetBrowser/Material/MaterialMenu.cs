using Rinten.Resources;

namespace Editor;

internal static class MaterialMenu
{
	[Event( "asset.contextmenu", Priority = 50 )]
	public static void OnMaterialFileAssetContext( AssetContextMenu e )
	{
		//Are all the files we have selected image assets?
		if ( !e.SelectedList.All( x => x.AssetType == AssetType.ImageFile ) )
			return;

		//A texture set - colour, normal, roughness - is one material and not three, so what is counted
		//here is the materials that would be written rather than the pictures that were picked.
		var groups = GroupByBaseName( e.SelectedList );

		if ( groups.Count > 1 )
			e.Menu.AddOption( $"Create Materials ({groups.Count})", "image", action: () => CreateMaterials( groups ) );
		else if ( groups.Count == 1 )
			e.Menu.AddOption( $"Create Material", "image", action: () => CreateMaterialUsingImageFiles( groups[0] ) );

		e.Menu.AddOption( $"Create Texture", "texture", action: () => CreateTextureUsingImageFiles( e.SelectedList ) );

		if ( e.SelectedList.Count == 1 )
		{
			e.Menu.AddOption( $"Create Sprite", "emoji_emotions", action: () => CreateSpriteUsingImageFiles( e.SelectedList ) );
		}
		else
		{
			var menuSprite = e.Menu.AddMenu( $"Create Sprite", "emoji_emotions" );
			menuSprite.AddOption( $"Create Sprite", "emoji_emotions", action: () => CreateSpriteUsingImageFiles( e.SelectedList ) );
			menuSprite.AddOption( $"Create Animation", "directions_run", action: () => CreateSpriteUsingImageFiles( e.SelectedList, true ) );
		}
	}

	//A Rinten.TextureDefinition beside each picked image - what this engine reads. It
	//used to write a .vtex , which nothing here loads and nothing here compiles.
	private static void CreateTextureUsingImageFiles( IEnumerable<AssetEntry> entries )
	{
		var first = entries.First().Asset;

		var fd = new FileDialog( null );
		fd.Title = "Create Texture from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( first.AbsolutePath );
		fd.DefaultSuffix = ".tex";
		fd.SelectFile( $"{first.Name}.tex" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Texture File (*.tex)" );

		if ( !fd.Execute() )
			return;

		var folder = System.IO.Path.GetDirectoryName( fd.SelectedFile );
		Asset asset = null;

		foreach ( var entry in entries )
		{
			var path = entries.Count() == 1
				? fd.SelectedFile
				: System.IO.Path.Combine( folder, $"{entry.Asset.Name}.tex" );

			asset = CreateTextureDefinition( entry.Asset, path ) ?? asset;
		}

		TreeBrowser.TreeAssetBrowser.RefreshOpen();
		TreeBrowser.TreeAssetBrowser.Reveal( asset );
		EditorUtility.InspectorObject = asset;
	}

	//Names that mean the picture holds measurements rather than colour.
	private static readonly string[] LinearSuffixes = { "_normal", "_nrm", "_rough", "_rma", "_orm", "_metal", "_metallic", "_ao", "_mask", "_height" };

	private static Asset CreateTextureDefinition( Asset image, string path )
	{
		var asset = AssetSystem.CreateResource( "tex", path );
		if ( asset is null )
			return null;

		if ( asset.TryLoadResource<TextureDefinition>( out var texture ) )
		{
			texture.Source = image.RelativePath;
			texture.Srgb = !LinearSuffixes.Any( x => image.Name.EndsWith( x, StringComparison.OrdinalIgnoreCase ) );

			asset.SaveToDisk( texture );
		}

		return asset;
	}

	//Suffixes that name one map inside a texture set rather than the set itself.
	private static readonly string[] MapSuffixes =
	{
		"color", "colour", "albedo", "basecolor", "diff", "diffuse", "ao", "occ", "occlusion", "amb", "normal", "nrm",
		"metallic", "metalness", "metal", "rough", "roughness", "rma", "orm", "spec", "selfillum",
		"emission", "emissive", "height", "mask"
	};

	//Names that mean the picture is the colour map of its set.
	private static readonly string[] ColorNames = { "_color", "_colour", "_albedo", "_basecolor", "_diff" };

	//The texture set a picture belongs to - its own name, with the map suffix taken off.
	private static string BaseNameOf( string name )
	{
		foreach ( var t in MapSuffixes )
		{
			if ( name.EndsWith( $"_{t}", StringComparison.OrdinalIgnoreCase ) )
				return name.Substring( 0, name.Length - (t.Length + 1) );
		}

		return name;
	}

	//The picked images gathered into texture sets - one per material that would be written - each led by the picture its colour
	//map should come from. Two folders may hold a set of the same name, so the folder is part of what tells one set from another.
	private static List<(string Name, string Folder, Asset Asset)> GroupByBaseName( IEnumerable<AssetEntry> entries )
	{
		var groups = new List<(string Name, string Folder, Asset Asset)>();

		var bySet = entries
			.Where( x => x.Asset is not null )
			.GroupBy( x => (Folder: System.IO.Path.GetDirectoryName( x.Asset.AbsolutePath ), Name: BaseNameOf( x.Asset.Name )) );

		foreach ( var set in bySet )
		{
			//A material falls back to the picture it was made from when nothing in the folder is named like a colour map, so the colour map leads its set.
			var lead = set.FirstOrDefault( x => ColorNames.Any( n => x.Asset.Name.Contains( n, StringComparison.OrdinalIgnoreCase ) ) ) ?? set.First();

			groups.Add( (set.Key.Name, set.Key.Folder, lead.Asset) );
		}

		return groups;
	}

	//One material per texture set among the picked images, each named after its set and written beside it. Nothing is asked for
	//- the names are already in the pictures - and a material that is on disk already is left alone rather than written over.
	private static void CreateMaterials( List<(string Name, string Folder, Asset Asset)> groups )
	{
		Asset last = null;
		var skipped = 0;

		foreach ( var group in groups )
		{
			if ( System.IO.File.Exists( System.IO.Path.Combine( group.Folder, $"{group.Name}.mat" ) ) )
			{
				skipped++;
				continue;
			}

			last = WriteMaterial( group.Asset, group.Name, group.Folder, group.Name ) ?? last;
		}

		if ( skipped > 0 )
			Log.Info( $"Created {groups.Count - skipped} material(s); left {skipped} alone, already on disk" );

		if ( last is null )
			return;

		TreeBrowser.TreeAssetBrowser.RefreshOpen();
		TreeBrowser.TreeAssetBrowser.Reveal( last );
		EditorUtility.InspectorObject = last;
	}

	private static void CreateMaterialUsingImageFiles( (string Name, string Folder, Asset Asset) group )
	{
		var asset = group.Asset;
		var assetName = group.Name;

		//Ask only for a name; the material goes in the image's own folder
		var folder = group.Folder;

		EditorModal.Prompt( null, "Create Material", null, chosen =>
		{
			var result = WriteMaterial( asset, assetName, folder, chosen );
			if ( result is null )
				return;

			TreeBrowser.TreeAssetBrowser.RefreshOpen();
			TreeBrowser.TreeAssetBrowser.Reveal( result );
			EditorUtility.InspectorObject = result;
		}, okay: "Create", initial: assetName, icon: "image" );
	}

	//Writes the material beside the picture it was made from, its maps filled in from whatever is named like it in the same folder.
	private static Asset WriteMaterial( Asset asset, string assetName, string folder, string chosen )
	{
		if ( string.IsNullOrWhiteSpace( chosen ) ) return null;

		var name = chosen.EndsWith( ".mat", StringComparison.OrdinalIgnoreCase ) ? chosen : chosen + ".mat";
		var file = System.IO.Path.Combine( folder, name );

		//Find all the image files in the same folder as the first asset we selected. The folder keeps its case and is compared without it:
		//lowercased, it matched no path with a capital in it, and every material came out with nothing but the picture that was clicked.
		var assetPath = System.IO.Path.GetDirectoryName( asset.AbsolutePath ).NormalizeFilename( false, false ) + "/";
		var assetPeers = AssetSystem.All.Where( x => x.AssetType == AssetType.ImageFile ).Where( x => x.AbsolutePath.StartsWith( assetPath, StringComparison.OrdinalIgnoreCase ) ).ToArray();
		var assetPeersWithSameBaseName = assetPeers.Where( x => x.Name == assetName || x.Name.StartsWith( assetName + "_" ) ).ToArray();
		if ( assetPeersWithSameBaseName.Length > 0 )
		{
			assetPeers = assetPeersWithSameBaseName;
		}

		//Try to work out what textures should go where using hacks and magic

		string texColor = FindTexture( assetPeers, "_color", "_basecolor", "_albedo", "_diff" );
		texColor ??= asset.RelativePath; //Failing that, lets use whatever we have selected, since they most likely selected the color one right

		string texNormal = FindTexture( assetPeers, "_nrm", "_normal" );

		//Roughness, metalness and occlusion are a picture each, read in red the way the standard shader reads them. A packed
		//_rma or _orm map keeps its three in channels none of those reads, so it is left out rather than read as roughness.
		string texRough = FindTexture( assetPeers, "_rough" );
		string texMetal = FindTexture( assetPeers, "_metal" );
		string texAo = FindTexture( assetPeers, "_ao", "_occ", "_amb" );

		string texEmission = FindTexture( assetPeers, "_selfillum", "_emission", "_emissive" );

		System.IO.File.WriteAllText( file, MaterialFileContent( texColor, texNormal, texRough, texMetal, texAo, texEmission ) );

		return AssetSystem.RegisterFile( file );
	}

	//Returns first asset which name contains provided fragment, or null if it found nothing.
	private static string FindTexture( IEnumerable<Asset> assets, params string[] fragments )
	{
		return assets.FirstOrDefault( x => fragments.Any( f => x.Name.Contains( f ) ) )?.RelativePath;
	}

	private static async void CreateSpriteUsingImageFiles( IEnumerable<AssetEntry> entries, bool isAnimation = false )
	{
		var asset = entries.First().Asset;
		var assetName = asset.Name;

		var fd = new FileDialog( null );
		fd.Title = "Create Sprite from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".sprite";
		fd.SelectFile( $"{assetName}.sprite" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Sprite File (*.sprite)" );

		if ( !fd.Execute() )
			return;

		if ( isAnimation )
		{
			var textures = entries.Select( x => TextureFromImageFile( x ) ).Where( x => x is not null ).ToArray();
			var sprite = Sprite.FromTextures( textures );
			var json = sprite.Serialize().ToJsonString();
			System.IO.File.WriteAllText( fd.SelectedFile, json );

			asset = AssetSystem.RegisterFile( fd.SelectedFile );
			while ( !asset.IsCompiledAndUpToDate )
			{
				await Task.Delay( 10 );
			}
		}
		else
		{
			//Export individual sprites
			foreach ( var p in entries )
			{
				var texture = TextureFromImageFile( p );
				if ( texture is null ) continue;

				var sprite = Sprite.FromTexture( texture );
				var json = sprite.Serialize().ToJsonString();
				var outPath = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( fd.SelectedFile ), System.IO.Path.GetFileNameWithoutExtension( p.Asset.AbsolutePath ) + ".sprite" );
				System.IO.File.WriteAllText( outPath, json );

				asset = AssetSystem.RegisterFile( outPath );
				while ( !asset.IsCompiledAndUpToDate )
				{
					await Task.Delay( 10 );
				}
			}
		}

		TreeBrowser.TreeAssetBrowser.RefreshOpen();
		TreeBrowser.TreeAssetBrowser.Reveal( asset );
		EditorUtility.InspectorObject = asset;
	}

	//Writes a .mat with MaterialDefinition properties; empty maps are omitted to use type defaults
	internal static string MaterialFileContent( string color, string normal = null, string rough = null, string metal = null,
		string occlusion = null, string emission = null )
	{
		var content = new Dictionary<string, object>();

		//Named rather than left out. Blank meant the standard surface to the renderer
		//and nothing at all to the inspector - see MaterialDefinition.StandardShader.
		content["Shader"] = Rinten.MaterialDefinition.StandardShader;

		if ( !string.IsNullOrEmpty( color ) ) content["ColorMap"] = color;
		if ( !string.IsNullOrEmpty( normal ) ) content["Normal"] = normal;
		if ( !string.IsNullOrEmpty( emission ) ) content["Emission"] = emission;

		//Under the names the standard shader asks for them by, which is what turns each one's feature on.
		var pictures = new Dictionary<string, string>();
		if ( !string.IsNullOrEmpty( rough ) ) pictures["g_tRoughness"] = rough;
		if ( !string.IsNullOrEmpty( metal ) ) pictures["g_tMetalness"] = metal;
		if ( !string.IsNullOrEmpty( occlusion ) ) pictures["g_tAO"] = occlusion;
		if ( pictures.Count > 0 ) content["Textures"] = pictures;

		//Metalness is a switch as well as a number - complex.shader's F_METALNESS is it being above nought - so a metal map comes with it on.
		if ( !string.IsNullOrEmpty( metal ) ) content["Metalness"] = 1.0f;

		content["__version"] = 2;

		return Json.Serialize( content );
	}

	private static Texture TextureFromImageFile( AssetEntry entry )
	{
		var generator = new ImageFileGenerator
		{
			FilePath = entry.Asset.RelativePath
		};

		return generator.Create( ResourceGenerator.Options.Default );
	}
}
