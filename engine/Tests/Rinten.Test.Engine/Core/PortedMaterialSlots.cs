using System.Collections.Generic;
using Rinten;
using Rinten.Porting.SBox;

using Slot = Rinten.Porting.SBox.SBoxMaterialSlots.Slot;

namespace EngineTests;

//Ported material slots are inferred from parameter names for any shader, not a fixed list
[TestClass]
[DoNotParallelize]
public class PortedMaterialSlotsTests
{
	private static Slot Of( string key )
	{
		return SBoxMaterialSlots.Of( key, out _ );
	}

	private static int LayerOf( string key )
	{
		SBoxMaterialSlots.Of( key, out var layer );
		return layer;
	}

	//The same slot under every spelling four different shaders give it.
	[TestMethod]
	public void ASlotIsFoundWhateverTheShaderCallsIt()
	{
		foreach ( var key in new[] { "TextureColor", "Color", "g_tColor", "TextureAlbedo", "Albedo",
			"BaseColor", "Diffuse", "TextureTranslucency", "g_tBaseColorMap" } )
		{
			Assert.AreEqual( Slot.Colour, Of( key ), key );
		}

		foreach ( var key in new[] { "TextureNormal", "Normal", "NormalMap", "Bump", "g_tNormalMap" } )
			Assert.AreEqual( Slot.Normal, Of( key ), key );

		foreach ( var key in new[] { "TextureRoughness", "Rough", "Roughness", "g_tGloss", "Smoothness" } )
			Assert.AreEqual( Slot.Roughness, Of( key ), key );

		foreach ( var key in new[] { "TextureMetalness", "Metal", "g_tMetalnessReflectanceFresnel" } )
			Assert.AreEqual( Slot.Metalness, Of( key ), key );

		foreach ( var key in new[] { "TextureAmbientOcclusion", "AO", "g_tOcclusion" } )
			Assert.AreEqual( Slot.Occlusion, Of( key ), key );

		foreach ( var key in new[] { "TextureEmissiveMask", "TextureSelfIllumMask", "SelfIllum",
			"Emissive", "g_tGlow" } )
			Assert.AreEqual( Slot.Emission, Of( key ), key );
	}

	//A layer letter is read off the end, and only where taking it off leaves something that means anything.
	[TestMethod]
	public void ALayerLetterIsReadAndNotInvented()
	{
		Assert.AreEqual( Slot.Colour, Of( "TextureColorA" ) );
		Assert.AreEqual( 0, LayerOf( "TextureColorA" ) );

		Assert.AreEqual( Slot.Colour, Of( "TextureColorD" ) );
		Assert.AreEqual( 3, LayerOf( "TextureColorD" ) );

		Assert.AreEqual( Slot.Normal, Of( "TextureNormalB" ) );
		Assert.AreEqual( 1, LayerOf( "TextureNormalB" ) );

		//The letter comes off only when what is left says something. A slot whose name happens to end in one keeps it.
		Assert.AreEqual( 0, LayerOf( "TextureColor" ), "nothing to take off" );
	}

	//Mask parameters (TextureTintMask, TextureBlendMask) are not surface maps
	[TestMethod]
	public void AMaskIsNotASurface()
	{
		Assert.AreEqual( Slot.None, Of( "TextureTintMask" ) );
		Assert.AreEqual( Slot.None, Of( "TextureTintMaskA" ) );
		Assert.AreEqual( Slot.None, Of( "TextureBlendMaskA" ) );
		Assert.AreEqual( Slot.None, Of( "g_vTexCoordScale2" ) );
		Assert.AreEqual( Slot.None, Of( "g_flBlendSoftnessB" ) );

		Assert.AreEqual( Slot.Emission, Of( "TextureEmissiveMask" ), "theirs calls the emission map a mask" );
	}

	//Blended materials use the first layer's textures, and bare unbracketed values parse as numbers
	[TestMethod]
	public void ANumberWithoutBracketsIsStillANumber()
	{
		var authored = new List<KeyValuePair<string, string>>
		{
			new( "g_tColor", "weapons/pallete.png" ),
			new( "g_flEmissiveIntensity", "1.000" ),
			new( "g_flRoughnessMultiplier", "0.600" ),
			new( "g_flMetalness", "1" ),
		};

		var slots = SBoxMaterialSlots.Read( authored );

		Assert.AreEqual( "weapons/pallete.png", SBoxMaterialSlots.Texture( slots, Slot.Colour, out _ ),
			"a real path is still a path" );

		Assert.AreEqual( "", SBoxMaterialSlots.Texture( slots, Slot.Emission, out var glow ),
			"an emission of one is not a picture called 1.000" );

		Assert.IsNotNull( glow );
		Assert.AreEqual( 1.0f, glow.Value.r, 0.0001f );

		Assert.AreEqual( 0.6f, SBoxMaterialSlots.Scalar( slots, Slot.Roughness ) ?? -1.0f, 0.0001f,
			"the roughness a material was given is the roughness it should arrive with" );

		Assert.AreEqual( 1.0f, SBoxMaterialSlots.Scalar( slots, Slot.Metalness ) ?? -1.0f, 0.0001f );
	}

	[TestMethod]
	public void ABlendedMaterialGivesUpItsFirstLayer()
	{
		var authored = new List<KeyValuePair<string, string>>
		{
			new( "F_MULTIBLEND", "3" ),
			new( "g_flTintColorA", "[1.000000 1.000000 1.000000 0.000000]" ),
			new( "TextureAmbientOcclusionA", "materials/default/default_ao.tga" ),
			new( "TextureBlendMaskA", "materials/default/default_blend.tga" ),
			new( "TextureColorA", "materials/grass_blend/forest_grass.png" ),
			new( "TextureNormalA", "materials/default/default_normal.tga" ),
			new( "TextureRoughnessA", "[1.000000 1.000000 1.000000 0.000000]" ),
			new( "TextureTintMaskA", "materials/default/default.tga" ),
			new( "TextureColorB", "materials/grass_blend/cliff.png" ),
			new( "TextureColorC", "materials/grass_blend/train_dirt.png" ),
			new( "TextureColorD", "materials/grass_blend/forest_dirt.png" ),
		};

		var slots = SBoxMaterialSlots.Read( authored );

		Assert.AreEqual( "materials/grass_blend/forest_grass.png",
			SBoxMaterialSlots.Texture( slots, Slot.Colour, out var tint ),
			"the first layer's colour, not the fourth and not the tint" );

		Assert.IsNull( tint, "a picture beats a constant, so there is no constant to report" );

		Assert.AreEqual( "materials/default/default_normal.tga",
			SBoxMaterialSlots.Texture( slots, Slot.Normal, out _ ) );

		//Written as a number rather than a picture, which is a roughness of one.
		Assert.AreEqual( "", SBoxMaterialSlots.Texture( slots, Slot.Roughness, out var rough ) );
		Assert.IsNotNull( rough );
		Assert.AreEqual( 1.0f, SBoxMaterialSlots.Scalar( slots, Slot.Roughness ).Value, 0.001f );

		Assert.AreEqual( 4, SBoxMaterialSlots.Layers( slots ), "four layers were named" );
	}
}
