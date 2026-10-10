using System;
using Rinten;

namespace EngineTests;

//Offscreen drawing into a bitmap for thumbnails; camera components can't be made in this host
[TestClass]
[DoNotParallelize]
public class ScenePreviewTest
{
	//What a view drew, read back and put in a bitmap - the last step of a thumbnail, and the one that has a layout to get wrong.
	[TestMethod]
	public void ReadsAViewIntoABitmap()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		if ( !CoreRenderDevice.Initialize( validation: true ) )
		{
			Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
			return;
		}

		var view = 0ul;

		try
		{
			view = CoreRenderDevice.CreateView( 32, 32 );

			Assert.AreNotEqual( 0ul, view, "a view needs no window" );

			var background = new Color( 20 / 255.0f, 150 / 255.0f, 90 / 255.0f );

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, background ) );
			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[32 * 32 * 4];

			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			using var bitmap = new Bitmap( 32, 32 );

			bitmap.SetPixelsRgba( pixels );

			//Red where red was asked for, and the same in both corners - which is how a picture that came out sideways or a channel short would show.
			foreach ( var (x, y) in new[] { (0, 0), (31, 0), (0, 31), (16, 16) } )
			{
				var pixel = bitmap.GetPixel( x, y );

				Assert.AreEqual( ScreenColour.Shown( background.r ), pixel.r, 0.02f, $"red at {x},{y}" );
				Assert.AreEqual( ScreenColour.Shown( background.g ), pixel.g, 0.02f, $"green at {x},{y}" );
				Assert.AreEqual( ScreenColour.Shown( background.b ), pixel.b, 0.02f, $"blue at {x},{y}" );
			}

			//A bitmap that holds more than eight bits a channel is not what a finished picture goes into, and says so rather than writing half of itself.
			using var deep = new Bitmap( 32, 32, floatingPoint: true );

			Assert.ThrowsException<InvalidOperationException>( () => deep.SetPixelsRgba( pixels ) );

			//Neither is one that is bigger than the picture.
			using var big = new Bitmap( 64, 64 );

			Assert.ThrowsException<ArgumentException>( () => big.SetPixelsRgba( pixels ) );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
			CoreRenderDevice.Shutdown();
		}
	}

	//Asked to draw nothing, it draws nothing - and says so rather than throwing, because a thumbnail that cannot be made is an ordinary thing.
	[TestMethod]
	public void RefusesWhatItCannotDraw()
	{
		using var preview = new CoreScenePreview();
		using var bitmap = new Bitmap( 16, 16 );

		Assert.IsFalse( preview.Render( null, null, bitmap ) );
		Assert.IsFalse( preview.Render( null, null, null ) );

		//Nor into an image that holds more than a screen can show.
		using var deep = new Bitmap( 16, 16, floatingPoint: true );

		Assert.IsFalse( preview.Render( null, null, deep ) );
	}
}
