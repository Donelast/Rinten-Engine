using Editor;
using Rinten;

namespace EngineTests.Core;

//A pixmap keeps its own pixels; a disposed source must not take them with it.
[TestClass]
[DoNotParallelize]
public class PixmapTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;
	}

	[TestMethod]
	public void DrawingADisposedBitmapDoesNotThrow()
	{
		using var dest = new Bitmap( 4, 4 );
		var source = new Bitmap( 4, 4 );
		source.Dispose();

		dest.DrawBitmap( source, dest.Rect );
	}

	[TestMethod]
	public void UpdateFromPixelsKeepsACopyAfterTheSourceIsDisposed()
	{
		var pixmap = new Pixmap( 2, 2 );
		var source = new Bitmap( 2, 2 );
		source.SetPixel( 0, 0, Color.Red );

		Assert.IsTrue( pixmap.UpdateFromPixels( source ) );
		source.Dispose();

		Assert.IsTrue( pixmap.IsValid );
		Assert.AreEqual( Color.Red, pixmap.picture.GetPixel( 0, 0 ) );
	}

	[TestMethod]
	public void UpdateFromPixelsReusesTheSameBitmapWhenTheSizeStays()
	{
		var pixmap = new Pixmap( 2, 2 );
		var first = pixmap.picture;

		using var source = new Bitmap( 2, 2 );
		source.SetPixel( 0, 0, Color.Blue );

		Assert.IsTrue( pixmap.UpdateFromPixels( source ) );
		Assert.AreSame( first, pixmap.picture );
		Assert.AreEqual( Color.Blue, pixmap.picture.GetPixel( 0, 0 ) );
	}
}
