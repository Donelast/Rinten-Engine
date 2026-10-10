using System.Linq;
using System.IO;

namespace Rinten;

public sealed partial class MaterialDefinition
{
	/// <summary>Image files a material map will take, as a file picker filter.</summary>
	public const string ImageFiles = "png,jpg,jpeg,tga,bmp,gif,webp";

	/// <summary>One map slot the inspector shows for a shader.</summary>
	public readonly record struct MapSlot( string Property, string Title, string Feature = null,
		string Texture = null )
	{
		/// <summary>Whether the shader asked for this one rather than it being one of the four every material carries.</summary>
		public bool IsDeclared => Texture is not null;

		//Only two of the slots are colour: the base colour and what the surface emits. A normal, a rough/metal pack and anything a shader asked for itself are all data - a mask means what its texel says, and running it through sRGB would make a half-strength mask sample at a fifth.
		/// <summary>Whether what goes in here is a colour the eye reads rather than numbers a shader does.</summary>
		public bool IsColour => Property is nameof( ColorMap ) or nameof( Emission );
	}

	static readonly MapSlot ColorSlot = new( nameof( ColorMap ), "Color" );
	static readonly MapSlot NormalMap = new( nameof( Normal ), "Normal", "F_NORMAL_MAP" );
	static readonly MapSlot Glow = new( nameof( Emission ), "Emission", "F_EMISSION" );

	static readonly MapSlot[] Standard = [ColorSlot, NormalMap, Glow];
	static readonly MapSlot[] ColorOnly = [ColorSlot];

	//A leaf: colour with the holes in its alpha, and a shape for it. Nothing glows.
	static readonly MapSlot[] Foliage = [ColorSlot, NormalMap];

	//A pane: colour and a shape for it. Its roughness and how much it mirrors are numbers rather than pictures - see glass.shader.
	static readonly MapSlot[] Glass = [ColorSlot, NormalMap];

	/// <summary>The map fields this shader actually samples.</summary>
	public static IReadOnlyList<MapSlot> MapsFor( string shader )
	{
		var name = Path.GetFileNameWithoutExtension( shader ?? "" ).ToLowerInvariant();

		var slots = name switch
		{
			"unlit" or "vertex_color" => ColorOnly,
			"foliage" => Foliage,
			"glass" => Glass,
			_ => Standard,
		};

		return Bound( shader, slots );
	}

	//Map slots with the feature names the shader actually declares; unread slots are dropped
	static IReadOnlyList<MapSlot> Bound( string shader, MapSlot[] slots )
	{
		if ( Declared( shader ) is not { } said ) return slots;

		var made = new List<MapSlot>( slots.Length );

		foreach ( var slot in slots )
		{
			//Null when no declared feature covers the slot; shaders declaring no features keep the guesses
			var feature = said.Features.Length > 0 ? FeatureOver( said.Features, slot.Property ) : slot.Feature;

			if ( feature is null && !said.Samples.Contains( slot.Property ) ) continue;

			made.Add( slot with { Feature = feature } );
		}

		return made;
	}

	//Which of the three pictures every material carries a shader's own text samples, by the material's field - see Bound.
	private static string[] Sampled( string onDisk )
	{
		string text;

		try
		{
			text = CompiledFile.TextOf( onDisk );
		}
		catch ( System.IO.IOException )
		{
			return System.Array.Empty<string>();
		}

		return new[] { nameof( ColorMap ), nameof( Normal ), nameof( Emission ) }
			.Where( x => System.Text.RegularExpressions.Regex.IsMatch( text, $@"\b{BindingOf( x )}\b" ) )
			.ToArray();
	}

	//What frame.slang calls the picture in one of the three slots.
	private static string BindingOf( string field ) => field switch
	{
		nameof( ColorMap ) => "g_color",
		nameof( Normal ) => "g_normal",
		_ => "g_emission",
	};

	//Which feature a shader turns on when a material fills this field in.
	static string FeatureOver( CoreShaderFeatures.Feature[] features, string property )
	{
		foreach ( var feature in features )
		{
			if ( feature.What != CoreShaderFeatures.Implied.Map ) continue;

			if ( string.Equals( feature.Field, property, System.StringComparison.OrdinalIgnoreCase ) )
				return feature.Name;
		}

		return null;
	}

	/// <summary>One switch a shader says it can be built without, as the editor sees it.</summary>
	public readonly record struct ShaderFeature( string Name, string Title, string Group,
		int Least, int Most, string[] Names );

	//Parsed shader declarations cached by file write time, since the editor asks many times per change
	private static readonly Dictionary<string, (long Ticks, CoreShaderFeatures.Feature[] Features, CoreShaderParams.Parameter[] Numbers, string[] Samples)> declared
		= new( System.StringComparer.OrdinalIgnoreCase );

	//Cached shader file locations (null if not found); finding them per frame allocated heavily
	private static readonly Dictionary<string, string> sources = new( System.StringComparer.OrdinalIgnoreCase );

	//Forgets where the shaders were found. A shader file came, went or changed, and the one a name answers
	//to may be another now - a project's own copy of an engine shader is found before the engine's.
	internal static void ForgetShaderSources()
	{
		lock ( declared ) sources.Clear();
	}

	//The file a written shader name answers to, found once - see sources.
	private static string SourceOf( string written )
	{
		lock ( declared )
		{
			if ( sources.TryGetValue( written, out var known ) ) return known;
		}

		//Through the alias table, so a material naming one of their surfaces reaches the one that answers for it - see CoreSceneRenderer.ComboFor.
		var found = CoreShaderFile.Source( CoreShaderFile.Real( written ) )
			?? CoreModel.ResolveTextOnDisk( written );

		lock ( declared ) sources[written] = found;

		return found;
	}

	//The shader's own file, and what it says, or nothing where it has none.
	private static (CoreShaderFeatures.Feature[] Features, CoreShaderParams.Parameter[] Numbers, string[] Samples)? Declared( string shader )
	{
		var written = Path.ChangeExtension(
			string.IsNullOrWhiteSpace( shader ) ? StandardShader : shader, ".shader" )
			.Replace( '\\', '/' );

		var onDisk = SourceOf( written );

		if ( onDisk is null ) return null;

		long ticks;

		try
		{
			//A file that is not there any more reads as the year 1601 rather than as an error. It went since it was found, so it is looked for again.
			var when = System.IO.File.GetLastWriteTimeUtc( onDisk );

			if ( when.Year <= 1601 )
			{
				lock ( declared ) sources.Remove( written );

				onDisk = SourceOf( written );
				if ( onDisk is null ) return null;

				when = System.IO.File.GetLastWriteTimeUtc( onDisk );
			}

			ticks = when.Ticks;
		}
		catch ( System.IO.IOException )
		{
			ticks = 0;
		}

		lock ( declared )
		{
			if ( declared.TryGetValue( onDisk, out var held ) && held.Ticks == ticks )
				return (held.Features, held.Numbers, held.Samples);

			var read = (Ticks: ticks, Features: CoreShaderFeatures.Of( onDisk ), Numbers: CoreShaderParams.Of( onDisk ),
				Samples: Sampled( onDisk ));

			declared[onDisk] = read;

			return (read.Features, read.Numbers, read.Samples);
		}
	}

	//Read out of the shader file itself, which is where they are written - see CoreShaderFeatures. Empty for a shader with none, and for one that is not on this disk.
	/// <summary>The features the shader this material names declares.</summary>
	public static IReadOnlyList<ShaderFeature> FeaturesFor( string shader )
	{
		if ( Declared( shader ) is not { } said ) return System.Array.Empty<ShaderFeature>();

		var declared = said.Features;
		var made = new ShaderFeature[declared.Length];

		for ( var i = 0; i < declared.Length; i++ )
		{
			var feature = declared[i];
			var names = new string[( feature.Most - feature.Least ) + 1];

			for ( var value = feature.Least; value <= feature.Most; value++ )
				names[value - feature.Least] = feature.NameOf( value );

			made[i] = new ShaderFeature( feature.Name, Pretty( feature.Name ), feature.Group,
				feature.Least, feature.Most, names );
		}

		return made;
	}

	//F_NORMAL_MAP as somebody would say it out loud.
	private static string Pretty( string name )
	{
		var text = name.StartsWith( "F_", System.StringComparison.Ordinal ) ? name[2..] : name;
		var words = text.Split( '_', System.StringSplitOptions.RemoveEmptyEntries );

		for ( var i = 0; i < words.Length; i++ )
		{
			//A word somebody has already capitalised inside is a name they chose - ObjectHighlight is not
			//Objecthighlight - and only a word that is all one case is one this can do anything useful to.
			var word = words[i];
			var shouted = word.ToUpperInvariant() == word;
			var quiet = word.ToLowerInvariant() == word;

			if ( !shouted && !quiet ) continue;

			//An abbreviation is left as it is: F_AO is AO, not Ao.
			if ( shouted && word.Length <= 2 ) continue;

			word = word.ToLowerInvariant();
			words[i] = char.ToUpperInvariant( word[0] ) + word[1..];
		}

		return string.Join( ' ', words );
	}

	/// <summary>What this material has that feature set to, whether it said so or not.</summary>
	public int FeatureValue( string name )
	{
		if ( Features is not null && Features.TryGetValue( name, out var chosen ) ) return chosen;

		//Naming the picture turns the feature on. Not naming it leaves the feature where the shader put it, which is not the same as off.
		return Implied( name ) ? 1 : DeclaredDefault( Shader, name );
	}

	//Whether the slot a feature covers is filled, per the shader's declaration; names are the fallback
	bool Implied( string name )
	{
		if ( Declared( Shader ) is { } said )
		{
			foreach ( var feature in said.Features )
			{
				if ( !string.Equals( feature.Name, name, System.StringComparison.Ordinal ) ) continue;

				return feature.What switch
				{
					CoreShaderFeatures.Implied.Map => Holds( feature.Field ),
					CoreShaderFeatures.Implied.Flag => Switched( feature.Field ),
					_ => false,
				};
			}

			return false;
		}

		return name switch
		{
			"F_COLOR_MAP" => Holds( nameof( ColorMap ) ),
			"F_NORMAL_MAP" => Holds( nameof( Normal ) ),
			"F_EMISSION" => Holds( nameof( Emission ) ),
			_ => false,
		};
	}

	//Whether a feature's slot holds a picture; a flat colour slot comes from tint, so it doesn't count
	bool Holds( string field ) => field switch
	{
		nameof( ColorMap ) => CoreDevTextures.IsPicture( ColorMap ),
		nameof( Normal ) => Chose( Normal ),
		nameof( Emission ) => Chose( Emission ),
		null or "" => false,
		_ => !string.IsNullOrWhiteSpace( TextureValue( field ) ),
	};

	//Whether one of the material's own switches a feature is over is on.
	bool Switched( string field ) => field switch
	{
		nameof( Metalness ) => Metalness > 0.0f,
		_ => false,
	};

	//Whether a slot holds a chosen picture, not the engine stand-in every slot now gets
	static bool Chose( string path ) => CoreDevTextures.IsChosen( path );

	//Feature default as the shader declares it; read fresh so edited shaders answer correctly
	static int DeclaredDefault( string shader, string name )
	{
		if ( Declared( shader ) is not { } said ) return 0;

		foreach ( var feature in said.Features )
		{
			if ( string.Equals( feature.Name, name, System.StringComparison.Ordinal ) )
				return feature.Default;
		}

		return 0;
	}

	/// <summary>The map slots this shader samples, given what this material asked for.</summary>
	public IReadOnlyList<MapSlot> Maps()
	{
		var kept = new List<MapSlot>();

		foreach ( var slot in MapsFor( Shader ).Concat( DeclaredMaps( Shader ) ) )
		{
			if ( slot.Feature is not null && FeatureValue( slot.Feature ) <= 0 ) continue;

			kept.Add( slot );
		}

		return kept;
	}

	/// <summary>The pictures this shader asked for on top of the four every material has.</summary>
	public static IReadOnlyList<MapSlot> DeclaredMaps( string shader )
	{
		List<MapSlot> made = null;

		foreach ( var parameter in ParametersFor( shader ) )
		{
			if ( !parameter.Texture ) continue;

			made ??= new List<MapSlot>();
			made.Add( new MapSlot( null, parameter.Title, parameter.Feature, parameter.Name ) );
		}

		return made ?? (IReadOnlyList<MapSlot>)System.Array.Empty<MapSlot>();
	}

	/// <summary>What this material named for one of its shader's own pictures.</summary>
	public string TextureValue( string name )
	{
		if ( Textures is null || string.IsNullOrWhiteSpace( name ) ) return "";

		//Either the name the shader gave it or that name without its prefix, which is the pair the renderer looks under too - see CoreSceneRenderer.Indexed.
		if ( Textures.TryGetValue( name, out var path ) ) return path ?? "";

		var bare = CoreShaderParams.Bare( name );

		return bare != name && Textures.TryGetValue( bare, out path ) ? path ?? "" : "";
	}

	/// <summary>Names one of its shader's own pictures, or clears it.</summary>
	public void SetTexture( string name, string path )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return;

		Textures ??= new Dictionary<string, string>();

		//Written under the name the shader gave it, and the short spelling a material may have
		//used for the same slot taken out, so the two cannot disagree about what it holds.
		if ( CoreShaderParams.Bare( name ) is { } bare && bare != name ) Textures.Remove( bare );

		if ( string.IsNullOrWhiteSpace( path ) ) Textures.Remove( name );
		else Textures[name] = path;
	}


	/// <summary>One number a shader says a material can set, as the editor sees it.</summary>
	public readonly record struct ShaderParameter( string Name, string Title, string Group,
		int Width, bool Colour, Vector4 Default, float Least, float Most, string Property,
		string Feature = null, bool Texture = false, bool Boolean = false );

	/// <summary>The numbers the shader this material names declares.</summary>
	public static IReadOnlyList<ShaderParameter> ParametersFor( string shader )
	{
		if ( Declared( shader ) is not { } said ) return System.Array.Empty<ShaderParameter>();

		var declared = said.Numbers;
		var made = new ShaderParameter[declared.Length];

		for ( var i = 0; i < declared.Length; i++ )
		{
			var parameter = declared[i];

			made[i] = new ShaderParameter( parameter.Name, parameter.Title, parameter.Group,
				parameter.Width, parameter.Kind == CoreShaderParams.Kind.Colour,
				parameter.Default, parameter.Least, parameter.Most, FieldFor( parameter.Name ),
				parameter.Feature, parameter.IsTexture,
				parameter.Kind == CoreShaderParams.Kind.Bool );
		}

		return made;
	}

	/// <summary>The material's own field a declared number is really about, or null.</summary>
	public static string FieldFor( string name ) => name switch
	{
		"g_flRoughness" => nameof( Roughness ),
		"g_flMetalness" => nameof( Metalness ),
		"g_flEmissionStrength" => nameof( EmissionStrength ),
		"g_flAlphaCutoff" => nameof( AlphaCutoff ),
		_ => null,
	};

	/// <summary>What this material has that number set to, whether it said or not.</summary>
	public Vector4 NumberValue( ShaderParameter parameter )
	{
		if ( parameter.Property is not null )
		{
			var one = parameter.Property switch
			{
				nameof( Roughness ) => Roughness,
				nameof( Metalness ) => Metalness,
				nameof( EmissionStrength ) => EmissionStrength,
				_ => AlphaCutoff,
			};

			return new Vector4( one, one, one, one );
		}

		if ( Numbers is not null && Numbers.TryGetValue( parameter.Name, out var set ) ) return set;

		return parameter.Default;
	}

	/// <summary>Whether this property is one of the image map slots.</summary>
	public static bool IsMapProperty( string name )
	{
		return name is nameof( ColorMap ) or nameof( Normal ) or nameof( Emission );
	}
}
