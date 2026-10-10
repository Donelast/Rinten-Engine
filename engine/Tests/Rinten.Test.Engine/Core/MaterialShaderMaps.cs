using Rinten;

namespace EngineTests.Core;

[TestClass]
public class MaterialShaderMapsTests
{
	[TestMethod]
	public void AnEmptyShaderUsesTheStandardMaps()
	{
		var slots = MaterialDefinition.MapsFor( "" );

		Assert.AreEqual( 3, slots.Count );
		Assert.AreEqual( nameof( MaterialDefinition.ColorMap ), slots[0].Property );
		Assert.AreEqual( nameof( MaterialDefinition.Normal ), slots[1].Property );
		Assert.AreEqual( nameof( MaterialDefinition.Emission ), slots[2].Property );
	}

	[TestMethod]
	public void AnUnlitShaderOnlyAsksForColor()
	{
		var slots = MaterialDefinition.MapsFor( "materials/unlit.shader" );

		Assert.AreEqual( 1, slots.Count );
		Assert.AreEqual( nameof( MaterialDefinition.ColorMap ), slots[0].Property );
	}

	[TestMethod]
	public void MapPropertiesAreTheImageSlots()
	{
		Assert.IsTrue( MaterialDefinition.IsMapProperty( nameof( MaterialDefinition.ColorMap ) ) );
		Assert.IsFalse( MaterialDefinition.IsMapProperty( nameof( MaterialDefinition.Shader ) ) );
		Assert.IsFalse( MaterialDefinition.IsMapProperty( nameof( MaterialDefinition.Tint ) ) );
	}
}
