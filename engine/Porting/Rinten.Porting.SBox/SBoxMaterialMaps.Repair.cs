using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rinten.Porting.SBox;

/// <summary>Naming the roughness, metalness and occlusion pictures of materials that were ported before the porter named them.</summary>
public static class SBoxMaterialRepair
{
	private static readonly Rinten.Diagnostics.Logger log = new( "Porting" );

	//What a colour map's name can end in, longest first.
	private static readonly string[] colourEndings =
	{
		"_basecolor", "_base_color", "_diffuse", "_albedo", "_colour", "_color", "_col", "_d", "_c",
	};

	//Each map: what its picture can be called, in order of likelihood, and the name the surface shader reads it by.
	private static readonly (string Picture, string[] Endings)[] wanted =
	{
		( "g_tRoughness", new[] { "_rough", "_roughness", "_r" } ),
		( "g_tMetalness", new[] { "_metal", "_metalness", "_metallic", "_m" } ),
		( "g_tAO", new[] { "_ao", "_occlusion", "_ambientocclusion", "_ambient_occlusion" } ),
	};

	//What a picture beside a material can be, in order of preference.
	private static readonly string[] extensions = { ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".psd" };

	[ConCmd( "fill_material_maps", ConVarFlags.Protected,
		Help = "Name the loose rough/metal/ao pictures of ported materials as the maps their shader reads" )]
	public static void FillOpenProject()
	{
		var project = Project.Current;

		if ( project is null )
		{
			log.Warning( "no project is open" );
			return;
		}

		Fill( Path.Combine( project.GetRootPath(), "Assets" ) );
	}

	/// <summary>Names the pictures of every material under this assets folder that has them and names none. Returns how many were filled in.</summary>
	public static int Fill( string assets )
	{
		if ( string.IsNullOrWhiteSpace( assets ) || !Directory.Exists( assets ) )
		{
			log.Warning( $"{assets}: no such assets folder" );
			return 0;
		}

		var filled = 0;
		var already = 0;
		var nothing = 0;

		foreach ( var file in PortPaths.Walk( assets ) )
		{
			if ( !file.EndsWith( ".mat", StringComparison.OrdinalIgnoreCase ) ) continue;

			switch ( FillOne( file, assets ) )
			{
				case Result.Filled: filled++; break;
				case Result.HasThem: already++; break;
				default: nothing++; break;
			}
		}

		log.Info( $"{assets}: named the maps of {filled} material(s), {already} already named theirs, " +
			$"{nothing} had no pictures to name" );

		return filled;
	}

	private enum Result { Filled, HasThem, NothingToDo }

	private static Result FillOne( string file, string assets )
	{
		JsonNode root;

		try
		{
			root = JsonNode.Parse( File.ReadAllText( file ) );
		}
		catch ( Exception )
		{
			return Result.NothingToDo;
		}

		if ( root is not JsonObject material ) return Result.NothingToDo;

		var textures = material["Textures"] as JsonObject;

		foreach ( var (picture, _) in wanted )
		{
			if ( textures is not null && textures.ContainsKey( picture ) ) return Result.HasThem;
		}

		var resource = Resource( file, assets );
		var found = new Dictionary<string, string>();

		//The loose pictures the port carried across, named from the colour map: head_color.png is head, and the rest are named from that.
		var colour = material["ColorMap"]?.GetValue<string>() ?? "";
		var stem = string.IsNullOrWhiteSpace( colour ) ? null : Stem( colour );

		if ( !string.IsNullOrEmpty( stem ) )
		{
			foreach ( var (picture, endings) in wanted )
			{
				if ( Beside( assets, stem, endings ) is { } beside ) found[picture] = Resource( beside, assets );
			}
		}

		//Or the packed map an earlier port made: named by the line the material still has if nothing has saved it since, or by the name the port gave it.
		if ( found.Count == 0 )
		{
			var candidates = new[]
			{
				material["RoughMetalAmbient"]?.GetValue<string>(),
				SBoxMaterialMaps.NameFor( resource, "rmo" ),
			};

			foreach ( var candidate in candidates )
			{
				if ( string.IsNullOrWhiteSpace( candidate ) ) continue;

				var full = Path.Combine( assets, candidate.Replace( '/', Path.DirectorySeparatorChar ) );
				if ( !File.Exists( full ) ) continue;

				if ( SBoxMaterialMaps.Split( full, assets, resource ) is not { } split ) continue;

				found["g_tRoughness"] = split.Rough;
				found["g_tMetalness"] = split.Metal;
				found["g_tAO"] = split.Occlusion;
				break;
			}
		}

		if ( found.Count == 0 ) return Result.NothingToDo;

		if ( textures is null )
		{
			textures = new JsonObject();
			material["Textures"] = textures;
		}

		foreach ( var (picture, path) in found ) textures[picture] = path;

		//Metalness is a switch as well as a number - complex.shader's F_METALNESS is it being above nought - so a metal map comes with it on.
		var metalness = material["Metalness"] is JsonValue said && said.TryGetValue<double>( out var value ) ? value : 0.0;

		if ( found.ContainsKey( "g_tMetalness" ) && metalness <= 0.0 ) material["Metalness"] = 1.0;

		material.Remove( "RoughMetalAmbient" );

		File.WriteAllText( file, material.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );

		log.Info( $"{resource}: named {string.Join( ", ", found.Keys )}" );

		return Result.Filled;
	}

	//The resource path of a file under the assets folder.
	private static string Resource( string file, string assets )
		=> Path.GetRelativePath( assets, file ).Replace( '\\', '/' );

	//A colour map's path with the ending that marks it as one taken off - still a resource path, still without its extension.
	private static string Stem( string colour )
	{
		var without = colour.Replace( '\\', '/' );

		var dot = without.LastIndexOf( '.' );
		if ( dot > 0 ) without = without[..dot];

		foreach ( var ending in colourEndings )
		{
			if ( without.EndsWith( ending, StringComparison.OrdinalIgnoreCase ) )
				return without[..^ending.Length];
		}

		//No ending anybody recognises: the maps are probably named from the whole of it - colour.png beside colour_rough.png happens too.
		return without;
	}

	//The first picture beside this stem with one of these endings.
	private static string Beside( string assets, string stem, string[] endings )
	{
		foreach ( var ending in endings )
		{
			foreach ( var extension in extensions )
			{
				var candidate = Path.Combine( assets,
					( stem + ending + extension ).Replace( '/', Path.DirectorySeparatorChar ) );

				if ( File.Exists( candidate ) ) return candidate;
			}
		}

		return null;
	}
}
