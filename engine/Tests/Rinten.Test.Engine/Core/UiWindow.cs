using Rinten;
using Rinten.UI;

namespace EngineTests;

//Editor windows draw a panel tree with our renderer; tested via a readable view, main thread only
[TestClass]
[DoNotParallelize]
public class CoreUiWindowTests
{
	//A root laid out to a rect, the way a window lays its panels out.
	private static RootPanel Root( int width, int height )
	{
		var root = new UiWindowRoot();

		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();

		return root;
	}

	//Logs whether this driver supports window alpha; decides how the startup bar is drawn
	[TestMethod]
	public void ASurfaceSaysWhetherItCanBeSeenThrough()
	{
		if ( !CoreRenderDevice.Initialize() )
		{
			Assert.Inconclusive( "no renderer on this machine" );
			return;
		}

		using var window = CoreWindow.Create( "alpha", 200, 100, hidden: true );

		Assert.IsNotNull( window, "a hidden window should still be a window" );

		var swapchain = CoreRenderDevice.AttachWindow( window, transparent: true,
			widthPixels: 200, heightPixels: 100 );

		Assert.AreNotEqual( 0ul, swapchain, "the window should have become a swapchain" );

		try
		{
			var composited = CoreRenderDevice.IsComposited( swapchain );

			System.Console.WriteLine( $"composited windows: {composited}" );
		}
		finally
		{
			CoreRenderDevice.DetachWindow( swapchain );
		}
	}

	[TestMethod]
	public void APanelIsLaidOutToTheWindowRatherThanTheScreen()
	{
		var root = Root( 800, 600 );

		Assert.AreEqual( 1.0f, root.Scale, "a window's panels are measured in pixels" );
		Assert.AreEqual( 800, root.Box.Rect.Width, 0.5f );
		Assert.AreEqual( 600, root.Box.Rect.Height, 0.5f );

		//The same root at another size, as a window being dragged.
		root.PreLayout( new Rect( 0, 0, 400, 200 ) );
		root.CalculateLayout();
		root.PostLayout();

		Assert.AreEqual( 400, root.Box.Rect.Width, 0.5f );
		Assert.AreEqual( 200, root.Box.Rect.Height, 0.5f );
	}

	[TestMethod]
	public void AChildFillsWhatItsStyleAsksFor()
	{
		var root = new UiWindowRoot();

		var panel = root.AddChild<Panel>();
		panel.Style.Width = 120;
		panel.Style.Height = 40;
		panel.Style.Left = 10;
		panel.Style.Top = 20;
		panel.Style.Position = PositionMode.Absolute;

		root.PreLayout( new Rect( 0, 0, 300, 300 ) );
		root.CalculateLayout();
		root.PostLayout();

		var rect = panel.Box.Rect;

		Assert.AreEqual( 10, rect.Left, 0.5f );
		Assert.AreEqual( 20, rect.Top, 0.5f );
		Assert.AreEqual( 120, rect.Width, 0.5f );
		Assert.AreEqual( 40, rect.Height, 0.5f );
	}

	//Text is drawn into a picture that is transparent everywhere it is not. Every piece of text in the interface is a quad with this
	//picture on it. Where the picture has no alpha, every label sits in a box of its own colour - which is exactly what it looked like.
	[TestMethod]
	public void RasterisedTextIsTransparentAroundTheLetters()
	{
		var scope = new TextRendering.Scope( "Hello", Color.White, 24 );

		using var bitmap = new Bitmap( 128, 48 );

		bitmap.Clear( new Color( 0, 0, 0, 0 ) );
		bitmap.DrawText( scope, bitmap.Rect, TextFlag.LeftTop | TextFlag.DontClip );

		var pixels = bitmap.GetPixels32();

		Assert.AreEqual( 128 * 48, pixels.Length );

		//The bottom right corner is past the end of five letters at any font.
		var corner = pixels[( 47 * 128 ) + 127];

		Assert.AreEqual( 0, corner.a, "the picture has to be transparent where there is no letter" );

		//And something was drawn: the alpha is not simply zero everywhere.
		var drawn = 0;
		foreach ( var pixel in pixels ) if ( pixel.a > 8 ) drawn++;

		Assert.IsTrue( drawn > 20, $"only {drawn} pixels have any ink in them" );
	}

	//The whole way through: a panel with a colour, drawn, read back.
	[TestMethod]
	public void APanelIsDrawnWhereItWasLaidOut()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		using var window = CoreWindow.Create( "rinten ui test", 320, 180, hidden: true );

		if ( window is null )
		{
			Assert.Inconclusive( $"no window: {CoreWindow.LastError}" );
			return;
		}

		var view = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize() )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			const int size = 64;

			view = CoreRenderDevice.CreateView( size, size );
			Assert.AreNotEqual( 0ul, view );

			//A root with one panel covering the left half of it, in a colour nothing else in the picture is.
			var root = new UiWindowRoot();

			var panel = root.AddChild<Panel>();
			panel.Style.Position = PositionMode.Absolute;
			panel.Style.Left = 0;
			panel.Style.Top = 0;
			panel.Style.Width = size / 2;
			panel.Style.Height = size;
			panel.Style.BackgroundColor = Color.Red;

			root.PreLayout( new Rect( 0, 0, size, size ) );
			root.CalculateLayout();
			root.PostLayout();

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

			CoreSceneDrawer.DrawUi( root, size, size );

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[size * size * 4];
			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			//A quarter of the way across is inside the panel; three quarters is past it, and still the colour the view was cleared to.
			int At( int x, int y ) => ((y * size) + x) * 4;

			var inside = At( size / 4, size / 2 );
			var outside = At( (size * 3) / 4, size / 2 );

			Assert.AreEqual( ScreenColour.Byte( 1.0f ), pixels[inside + 0], 3, "the panel is red where it is" );
			Assert.AreEqual( 0, pixels[inside + 1], 3, "and only red" );

			Assert.AreEqual( 0, pixels[outside + 0], 3, "and nothing is drawn where it is not" );
		}
		finally
		{
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );
		}
	}

	//A label draws only its glyphs; losing alpha anywhere in the chain turns it into a solid box
	[TestMethod]
	public void ALabelDrawsLettersRatherThanABox()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		using var window = CoreWindow.Create( "rinten ui text test", 320, 180, hidden: true );

		if ( window is null )
		{
			Assert.Inconclusive( $"no window: {CoreWindow.LastError}" );
			return;
		}

		var view = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize() )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			const int size = 128;

			view = CoreRenderDevice.CreateView( size, size );
			Assert.AreNotEqual( 0ul, view );

			//Red behind, one word on top of it, near the top left.
			var root = new UiWindowRoot();
			root.Style.BackgroundColor = Color.Red;

			var label = root.AddChild<Label>();
			label.Text = "Hi";
			label.Style.Position = PositionMode.Absolute;
			label.Style.Left = 4;
			label.Style.Top = 4;
			label.Style.FontSize = 16;
			label.Style.FontColor = Color.White;

			root.PreLayout( new Rect( 0, 0, size, size ) );
			root.CalculateLayout();
			root.PostLayout();

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

			CoreSceneDrawer.DrawUi( root, size, size );

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[size * size * 4];
			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			//Inside the label's own quad, under the letters rather than beside them: a box behind the text would cover this, ink would not.
			var rect = label.Box.Rect;

			var x = (int)( rect.Left + ( rect.Width * 0.5f ) );
			var y = (int)( rect.Bottom - 2 );

			Assert.IsTrue( x > 0 && x < size && y > 0 && y < size, $"the label landed at {rect}" );

			var at = ((y * size) + x) * 4;

			Assert.AreEqual( ScreenColour.Byte( 1.0f ), pixels[at + 0], 4, "the panel behind should still be red" );
			Assert.AreEqual( 0, pixels[at + 1], 8, "and not washed out by a box behind the letters" );
		}
		finally
		{
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );
		}
	}

	//Label markup is parsed and rendered, not drawn as literal angle brackets
	[TestMethod]
	public void MarkupIsObeyedRatherThanSpelledOut()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		using var window = CoreWindow.Create( "rinten ui markup test", 320, 180, hidden: true );

		if ( window is null )
		{
			Assert.Inconclusive( $"no window: {CoreWindow.LastError}" );
			return;
		}

		var view = 0ul;

		try
		{
			if ( !CoreRenderDevice.Initialize() )
			{
				Assert.Inconclusive( $"no Vulkan device here: {CoreRenderDevice.Description}" );
				return;
			}

			const int size = 256;

			view = CoreRenderDevice.CreateView( size, size );
			Assert.AreNotEqual( 0ul, view );

			var root = new UiWindowRoot();
			root.Style.BackgroundColor = Color.Black;

			var label = root.AddChild<Label>();
			label.IsRich = true;
			label.Text = "<b>Hi</b>";
			label.Style.Position = PositionMode.Absolute;
			label.Style.Left = 4;
			label.Style.Top = 4;
			label.Style.Width = size - 8;
			label.Style.FontSize = 16;
			label.Style.FontColor = Color.White;

			root.PreLayout( new Rect( 0, 0, size, size ) );
			root.CalculateLayout();
			root.PostLayout();

			Assert.IsTrue( CoreRenderDevice.BeginViewFrame( view, Color.Black ) );

			CoreSceneDrawer.DrawUi( root, size, size );

			Assert.IsTrue( CoreRenderDevice.EndViewFrame( view ) );

			var pixels = new byte[size * size * 4];
			Assert.IsTrue( CoreRenderDevice.ReadView( view, pixels ) );

			//White letters on black: how far across the last one of them is.
			var rightmost = -1;
			var inked = 0;

			for ( var y = 0; y < size; y++ )
			{
				for ( var x = 0; x < size; x++ )
				{
					if ( pixels[(((y * size) + x) * 4) + 1] < 60 ) continue;

					inked++;
					if ( x > rightmost ) rightmost = x;
				}
			}

			Assert.IsTrue( inked > 20, $"only {inked} pixels have any letter in them" );

			//Two letters at this size are some twenty pixels wide; the markup written out is four times that.
			Assert.IsTrue( rightmost < 64, $"the letters run to {rightmost} pixels across - the markup is being drawn" );
		}
		finally
		{
			if ( view != 0 ) CoreRenderDevice.DestroyView( view );
		}
	}
}
