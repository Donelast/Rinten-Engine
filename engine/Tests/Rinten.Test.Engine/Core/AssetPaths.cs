using Editor;

namespace EngineTests;

[TestClass]
public class AssetPathsTest
{
	[TestMethod]
	public void AUnixAbsolutePathKeepsItsSlash()
	{
		var keys = AssetSystem.LookupKeys( "/home/me/proj/Assets/foo.mdl" );

		CollectionAssert.Contains( keys, "/home/me/proj/Assets/foo.mdl" );
	}

	[TestMethod]
	public void AContentPathStillDropsItsSlash()
	{
		var keys = AssetSystem.LookupKeys( "/weapons/awp/awp.mdl" );

		CollectionAssert.Contains( keys, "weapons/awp/awp.mdl" );
	}
}
