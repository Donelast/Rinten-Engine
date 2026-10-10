using System;
using System.IO;
using static Editor.AboutWidget;

namespace SystemTests;

[TestClass]
public class ThirdPartyLegalTest
{
	[TestMethod]
	public void CheckJsonValidity()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );
		//If we got this far without an exception, the JSON is valid
		Assert.IsNotNull( indexData );
		Assert.IsTrue( indexData.Components.Any(), "Dependency index deserialized to an empty component list" );
	}

	[TestMethod]
	public void CheckAllLicensesExist()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );
		Assert.IsTrue( indexData.Components.Any(), "Dependency index deserialized to an empty component list" );
		foreach ( var component in indexData.Components )
		{
			// We don't require a license for public domain components
			if ( component.License.ToLower() == "public-domain" || component.License.ToLower() == "proprietary" ) continue;
			var licensePath = Path.Combine( "thirdpartylegalnotices/licenses/", component.Name.ToLower().Replace( " ", "-" ) );
			Assert.IsTrue( File.Exists( licensePath ), $"License file missing: {licensePath}" );
		}
	}

	/// <summary>
	/// Every license file we ship should belong to a component that's still in the index. An orphan
	/// means a dependency was dropped without cleaning up after it.
	/// </summary>
	[TestMethod]
	public void CheckNoOrphanLicenses()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );

		var expected = indexData.Components
			.Select( x => x.Name.ToLower().Replace( " ", "-" ) )
			.ToHashSet();

		foreach ( var file in Directory.GetFiles( "thirdpartylegalnotices/licenses/" ) )
		{
			var name = Path.GetFileName( file );
			Assert.IsTrue( expected.Contains( name ), $"License file has no component in the index: {name}" );
		}
	}

	//Copyleft components have to be dynamically linked, so users can swap in their own build, and
	//have to tell people where to get the source we built from. Losing either puts us in breach.
	[TestMethod]
	public void CheckCopyleftComponentsAreCompliant()
	{
		var fileData = File.ReadAllText( "thirdpartylegalnotices/dependency_index.json" );
		var indexData = Json.Deserialize<DependencyIndex>( fileData );

		foreach ( var component in indexData.Components )
		{
			var license = component.License?.ToUpper() ?? "";
			if ( !license.Contains( "LGPL" ) && !license.Contains( "GPL" ) ) continue;

			Assert.AreEqual( "dynamic", component.Linkage,
				$"{component.Name} is {component.License} - it must be dynamically linked so it can be replaced" );

			Assert.IsFalse( string.IsNullOrWhiteSpace( component.SourceUrl ),
				$"{component.Name} is {component.License} - it must publish a SourceUrl for the corresponding source" );
		}
	}
}
