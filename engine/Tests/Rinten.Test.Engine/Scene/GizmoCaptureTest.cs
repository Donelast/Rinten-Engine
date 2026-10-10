namespace SceneTests;

//Scene view gizmos are recorded via Gizmo.Draw; a broken capture looks like no gizmos, no error
[TestClass]
[DoNotParallelize] //One active instance.
public class GizmoCaptureTest
{
	private static Gizmo.Instance Fresh()
	{
		ThreadSafe.MarkMainThread();

		return new Gizmo.Instance();
	}

	//Nothing is kept outside a recorded frame - a shortcut pushing the view's instance to ask where the pointer is must not replace what the view draws.
	[TestMethod]
	public void NothingIsKeptOutsideARecordedFrame()
	{
		using var instance = Fresh();

		using ( instance.Push() )
		{
			Gizmo.Draw.ScreenRect( new Rect( 0, 0, 10, 10 ), Color.White );
		}

		Assert.AreEqual( 0, instance.Draws.Flats.Count );
	}

	[TestMethod]
	public void ScreenRectIsKeptInTheRecordedFrame()
	{
		using var instance = Fresh();

		using ( instance.RecordDraws() )
		using ( instance.Push() )
		{
			Gizmo.Draw.ScreenRect( new Rect( 4, 8, 20, 30 ), Color.Red );
		}

		var shapes = instance.Draws.Flats;

		Assert.AreEqual( 1, shapes.Count );

		//Two triangles, no border asked for. The array is borrowed from the pool and is longer than the shape; the count is what says how much is its own.
		Assert.AreEqual( 6, shapes[0].Count );
	}

	//A border is drawn as its own bars rather than by a shader that knows what a border is, so asking for one gets more triangles, not different ones.
	[TestMethod]
	public void ScreenRectWithABorderIsMoreTriangles()
	{
		using var instance = Fresh();

		using ( instance.RecordDraws() )
		using ( instance.Push() )
		{
			Gizmo.Draw.ScreenRect( new Rect( 0, 0, 40, 40 ), Color.Transparent,
				default, Color.White, new Vector4( 2 ) );
		}

		var shapes = instance.Draws.Flats;

		Assert.AreEqual( 1, shapes.Count );

		//Four bars, no fill: the colour asked for was nothing.
		Assert.AreEqual( 24, shapes[0].Count );
	}

	//Screen shapes are drawn in the order they were asked for - a gizmo that sorts its own handles back to front is relying on it.
	[TestMethod]
	public void ShapesKeepTheOrderTheyWereDrawnIn()
	{
		using var instance = Fresh();

		using ( instance.RecordDraws() )
		using ( instance.Push() )
		{
			Gizmo.Draw.ScreenCircle( new Vector2( 50, 50 ), 8.0f, Color.White );
			Gizmo.Draw.ScreenLine( new Vector2( 0, 0 ), new Vector2( 10, 10 ), 2.0f, Color.White );
		}

		var shapes = instance.Draws.Flats;

		Assert.AreEqual( 2, shapes.Count );

		//A circle is many triangles, a line one quad.
		Assert.IsTrue( shapes[0].Count > 6 );
		Assert.AreEqual( 6, shapes[1].Count );
	}
}
