using Editor;
using Rinten;

namespace EngineTests;

//Our resource types keep their drawn icon instead of being overwritten by the png registry scan
[TestClass]
[DoNotParallelize]
public class EditorAssetTypeIconTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A type that drew its own icon keeps it when the asset scan finishes.
	[TestMethod]
	public void AGameResourceKeepsTheIconItDrewForItself()
	{
		var drawn = new Pixmap( 16, 16 );

		var type = new AssetType
		{
			FriendlyName = "Material Definition",
			FileExtension = "mat",
			IsGameResource = true,

			//What GenerateGlyphs leaves behind for a resource that draws itself.
			Icon16 = drawn,
			Icon64 = drawn,
			Icon128 = drawn,
			Icon256 = drawn,
		};

		//Every asset type is initialised again when the content scan finishes - see AssetSystem.AssetScanComplete.
		type.Init();

		Assert.AreSame( drawn, type.Icon16, "the icon it drew is the icon it keeps" );
		Assert.AreSame( drawn, type.Icon256, "and at every size" );
	}
}
