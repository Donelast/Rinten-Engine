using System;

namespace PackageTests;

//Live API: every package type's DTO must deserialize; deleted packages fail, so use stable ones
[TestClass]
[TestCategory( "LiveBackend" )]
public class PackageParsingTest
{
	//A model package - exercises ModelMetaData, including the BoundsMin/BoundsMax vectors which the website serializes as "x,y,z" strings.
	[TestMethod]
	public async Task ParseModelPackage()
	{
		var package = await Package.FetchAsync( "facepunch.watermelon", false );

		Assert.IsNotNull( package, "Failed to fetch model package" );

		var meta = package.Metadata as Package.ModelMetaData;
		Assert.IsNotNull( meta, "Expected ModelMetaData" );

		//Bounds should have parsed from the "x,y,z" string into a real vector, not zero.
		Assert.AreNotEqual( Vector3.Zero, meta.BoundsMin, "BoundsMin didn't parse" );
		Assert.AreNotEqual( Vector3.Zero, meta.BoundsMax, "BoundsMax didn't parse" );
	}

	//A material package - exercises MaterialMetaData.
	[TestMethod]
	public async Task ParseMaterialPackage()
	{
		var package = await Package.FetchAsync( "facepunch.glass_a", false );

		Assert.IsNotNull( package, "Failed to fetch material package" );
		Assert.IsInstanceOfType( package.Metadata, typeof( Package.MaterialMetaData ), "Expected MaterialMetaData" );
	}

	//A clothing package - exercises ClothingMetaData.
	[TestMethod]
	public async Task ParseClothingPackage()
	{
		var package = await Package.FetchAsync( "ducksworkshop.mobbosstop", false );

		Assert.IsNotNull( package, "Failed to fetch clothing package" );
		Assert.IsInstanceOfType( package.Metadata, typeof( Package.ClothingMetaData ), "Expected ClothingMetaData" );
	}

	//Fetch a spread of packages across every type and make sure each one deserializes without throwing. FetchAsync swallows
	//network/not-found errors (returns null) but lets a JSON deserialization exception propagate, so a parse regression fails this test.
	[TestMethod]
	[DataRow( "facepunch.watermelon" )]        //model
	[DataRow( "facepunch.oildrumexplosive" )]  //model
	[DataRow( "facepunch.glass_a" )]           //material
	[DataRow( "facepunch.metal_01a" )]         //material
	[DataRow( "ducksworkshop.mobbosstop" )]    //clothing
	[DataRow( "lies.swagchain" )]              //clothing
	[DataRow( "facepunch.flatgrass" )]         //map
	[DataRow( "facepunch.sandbox" )]           //game
	[DataRow( "facepunch.shatterglass" )]      //library
	[DataRow( "igrotronika.explosion2" )]      //sound
	[DataRow( "wmfg.powerkart" )]              //prefab
	public async Task FetchAndParse( string ident )
	{
		var package = await Package.FetchAsync( ident, false );

		Assert.IsNotNull( package, $"Failed to fetch/parse {ident}" );
		Console.WriteLine( $"{ident} -> {package.TypeName}, metadata: {package.Metadata?.GetType().Name ?? "none"}" );
	}
}
