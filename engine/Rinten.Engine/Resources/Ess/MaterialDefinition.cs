namespace Rinten;

/// <summary>A material in our own editable text format; compiled to the binary the renderer reads.</summary>
[AssetType( Name = "Material", Extension = "mat", Category = "Rendering", IconColor = "#7c5cd6", Icon = "format_paint" )]
public sealed partial class MaterialDefinition : GameResource
{
	//Offered as a list rather than as a path to type - see ShaderNameAttribute and MaterialDefinition.Shaders, which is what decides that a project's own surface belongs in it and the engine's shadow pass does not.
	/// <summary>Which shader draws this. Empty means the engine's standard one.</summary>
	[Property, ShaderName]
	public string Shader
	{
		get => shader;
		set => shader = Filled( value, StandardShader );
	}

	private string shader = StandardShader;

	//Written explicitly rather than left blank, so the inspector finds the shader's features
	/// <summary>The surface a material wears unless it names another.</summary>
	public const string StandardShader = "shaders/complex.shader";

	//Keyed by the shader's define (e.g. F_NORMAL_MAP); undeclared ones are ignored but kept
	/// <summary>Which of that shader's features this material turns on.</summary>
	[Property, Hide]
	public Dictionary<string, int> Features { get; set; } = new();

	//Overrides of shader-declared defaults; roughness, metalness etc. have their own fields
	/// <summary>The numbers its shader declared, by the name the shader gave them.</summary>
	[Property, Hide]
	public Dictionary<string, Vector4> Numbers { get; set; } = new();

	//Extra textures beyond the four core maps, e.g. "g_tHeight"; bindless, so they cost no binding
	/// <summary>The pictures a material sets on its shader, by the name the shader gave them.</summary>
	[Property, Hide]
	public Dictionary<string, string> Textures { get; set; } = new();


	/// <summary>Multiplied into the colour texture, or used on its own when there is none.</summary>
	[Property]
	public Color Tint { get; set; } = Color.White;

	//Never empty: the setter fills blanks with the engine's white texture; broken paths are kept
	/// <summary>Base colour. Named for the map rather than the colour so it does not shadow <see cref="Tint"/>.</summary>
	[Property, Group( "Textures" ), FilePath( ImageFiles ), Title( "Color" )]
	public string ColorMap
	{
		get => colorMap;
		set => colorMap = Filled( value, CoreDevTextures.White );
	}

	[Property, Group( "Textures" ), FilePath( ImageFiles )]
	public string Normal
	{
		get => normal;
		set => normal = Filled( value, CoreDevTextures.FlatNormal );
	}

	[Property, Group( "Textures" ), FilePath( ImageFiles )]
	public string Emission
	{
		get => emission;
		set => emission = Filled( value, CoreDevTextures.Black );
	}

	//Folded into the colour's alpha at compile time, so it costs no extra sample
	/// <summary>What the colour is cut by where it is not the colour's own alpha: a grey picture, white kept and black cut away, multiplied into that alpha. Empty for none.</summary>
	[Property, Group( "Textures" ), FilePath( ImageFiles ), Title( "Opacity Mask" )]
	public string OpacityMask { get; set; }

	private string colorMap = CoreDevTextures.White;
	private string normal = CoreDevTextures.FlatNormal;
	private string emission = CoreDevTextures.Black;

	//What was written, or the engine's own picture where that was nothing.
	private static string Filled( string written, string instead )
		=> string.IsNullOrWhiteSpace( written ) ? instead : written;

	/// <summary>Whether a slot holds one of the engine's own pictures rather than the project's.</summary>
	public static bool IsBuiltInMap( string path ) => CoreDevTextures.IsBuiltIn( path );

	//Nothing for a path that names a real file. A built-in has no file to find and no asset to ask, so anything drawing the slot has to be told the colour rather than left to look it up and come back empty-handed.
	/// <summary>The one colour that picture is, for showing a slot that holds one.</summary>
	public static Color? BuiltInMapColour( string path ) => CoreDevTextures.Colour( path );

	/// <summary>The path a map slot holding one flat colour is written as.</summary>
	public static string FlatMapPath( Color colour, bool srgb ) => CoreDevTextures.PathFor( colour, srgb );

	/// <summary>Whether a slot holds a colour somebody chose, rather than a picture or the stand-in a slot nobody filled in carries.</summary>
	public static bool IsPickedMapColour( string path ) => CoreDevTextures.IsPicked( path );

	//0 is mirror, 1 matte; with a roughness map it multiplies the map (result clamped to 1)
	/// <summary>How rough the surface is.</summary>
	[Property, Group( "Surface" ), Range( 0, 2 )]
	public float Roughness { get; set; } = 0.5f;

	[Property, Group( "Surface" ), Range( 0, 1 )]
	public float Metalness { get; set; } = 0.0f;

	[Property, Group( "Surface" )]
	public float EmissionStrength { get; set; } = 0.0f;

	/// <summary>What this feels like to walk on and bump into. Appearance and physics are set together because an artist changing stone to wood means both.</summary>
	[Property, Group( "Surface" ), ResourceType( "surface" )]
	public string PhysicsSurface { get; set; } = "";

	/// <summary>Below this a pixel is not drawn at all - read only where the shader's F_ALPHA is on.</summary>
	[Property, Group( "Rendering" ), Range( 0, 1 )]
	public float AlphaCutoff { get; set; } = 0.5f;

	//The tile in the asset browser. Overridden rather than declared on the attribute: the icon only lives on the obsolete GameResource attribute,
	//and the base implementation reads it from there - so a type using the current AssetType attribute gets a question mark unless it draws its own.
	protected override Bitmap CreateAssetTypeIcon( int width, int height )
		=> EssResource.DrawGlyph( "palette", width, height, "#b79bff" );
}
