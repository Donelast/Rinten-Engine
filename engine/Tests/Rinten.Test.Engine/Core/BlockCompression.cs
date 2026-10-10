using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;
using Rinten.Porting.SBox;

namespace EngineTests.Core;

//Every BC format decodes to a picture, including BC7's partitioned modes
[TestClass]
[DoNotParallelize]
public class BlockCompressionTests
{
	//A texture of theirs in a mode the managed decoder refuses.
	private static string Skin
	{
		get
		{
			var install = SBoxBaseContent.Find();
			if ( install is null ) return null;

			var folder = Path.Combine( install, "addons", "citizen", "Assets", "models", "citizen", "skin" );
			if ( !Directory.Exists( folder ) ) return null;

			return Directory.EnumerateFiles( folder, "*.vtex_c", SearchOption.AllDirectories )
				.FirstOrDefault( x => x.Contains( "color", StringComparison.OrdinalIgnoreCase ) );
		}
	}

	[TestMethod]
	public void EveryFormatIsRead()
	{
		//A block of zeroes is a legal block in every one of these - and in BC7 it is the
		//"no mode" block, which decodes to transparent black rather than being refused.
		foreach ( var kind in new[] { CoreBlockCompression.Kind.Bc1, CoreBlockCompression.Kind.Bc3,
			CoreBlockCompression.Kind.Bc4, CoreBlockCompression.Kind.Bc5, CoreBlockCompression.Kind.Bc7 } )
		{
			var block = new byte[CoreBlockCompression.BlockSize( kind )];

			Assert.IsNotNull( CoreBlockCompression.Decode( kind, block, 4, 4 ),
				$"the decoder says it cannot unpack {kind}" );
		}
	}

	[TestMethod]
	public void ACharactersSkinDecodes()
	{
		if ( Skin is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var bitmap = CoreImportedTexture.Load( Skin );

		Assert.IsNotNull( bitmap, $"{Path.GetFileName( Skin )} would not decode" );

		Assert.IsTrue( bitmap.Width > 16 && bitmap.Height > 16,
			$"{bitmap.Width}x{bitmap.Height} is not a character's skin" );

		//A picture rather than a field of one colour: a decoder that returns nothing useful tends to return the same byte everywhere.
		var pixels = bitmap.GetPixels32();

		Assert.IsNotNull( pixels );

		var first = pixels[0];
		var different = false;

		for ( var i = 0; i < pixels.Length; i += 97 )
		{
			if ( pixels[i] == first ) continue;

			different = true;
			break;
		}

		Assert.IsTrue( different, "every pixel came out the same, which is not a skin" );
	}
}
