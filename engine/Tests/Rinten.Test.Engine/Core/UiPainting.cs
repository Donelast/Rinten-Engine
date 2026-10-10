using Rinten.UI;
using System;
using System.Reflection;

namespace EngineTests;

//Reads back UI pixels from the device so paint bugs become numbers instead of impressions
[TestClass]
[DoNotParallelize]
public class UiPaintingTests
{
	private const int Size = 64;

	//Draws one panel, dressed however the test wants it, and hands back the pixels. Null where there is
	//nothing to draw with, which is a test that says so rather than one that fails on a machine with no device.
	private static byte[] Painted( Action<Panel> dress, Func<UiWindowRoot, Panel> make = null )
	{
		if ( !CoreNative.Available ) return null;

		using var window = CoreWindow.Create( "rinten ui painting", 320, 180, hidden: true );

		if ( window is null ) return null;
		if ( !CoreRenderDevice.Initialize() ) return null;

		var view = CoreRenderDevice.CreateView( Size, Size );

		if ( view == 0 ) return null;

		try
		{
			var root = new UiWindowRoot();

			var panel = make is null ? root.AddChild<Panel>() : make( root );
			panel.Style.Position = PositionMode.Absolute;
			panel.Style.Left = 8;
			panel.Style.Top = 8;
			panel.Style.Width = Size - 16;
			panel.Style.Height = Size - 16;

			dress( panel );

			root.PreLayout( new Rect( 0, 0, Size, Size ) );
			root.CalculateLayout();
			root.PostLayout();

			if ( !CoreRenderDevice.BeginViewFrame( view, Color.Black ) ) return null;

			CoreSceneDrawer.DrawUi( root, Size, Size );

			if ( !CoreRenderDevice.EndViewFrame( view ) ) return null;

			var pixels = new byte[Size * Size * 4];

			return CoreRenderDevice.ReadView( view, pixels ) ? pixels : null;
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
		}
	}

	private static void Rounded( Panel panel )
	{
		panel.Style.BorderTopLeftRadius = 12;
		panel.Style.BorderTopRightRadius = 12;
		panel.Style.BorderBottomLeftRadius = 12;
		panel.Style.BorderBottomRightRadius = 12;
	}

	private static byte Channel( byte[] pixels, int x, int y, int channel ) => pixels[( ( y * Size ) + x ) * 4 + channel];

	//A transparent border must not change the panel's colour or alpha (used to reserve hover space)
	[TestMethod]
	public void ATransparentBorderChangesNothing()
	{
		var plain = Painted( panel =>
		{
			panel.Style.BackgroundColor = Color.Red;
			Rounded( panel );
		} );

		if ( plain is null )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		var bordered = Painted( panel =>
		{
			panel.Style.BackgroundColor = Color.Red;
			Rounded( panel );
			panel.Style.Set( "border", "1px solid rgba( 0, 0, 0, 0 )" );
		} );

		Assert.IsNotNull( bordered );

		//The whole of one corner, ramp and all.
		for ( var y = 6; y < 16; y++ )
		{
			for ( var x = 6; x < 16; x++ )
			{
				Assert.AreEqual( Channel( plain, x, y, 0 ), Channel( bordered, x, y, 0 ), 2,
					$"a transparent border changed the panel at {x},{y}" );
			}
		}
	}

	//A border is drawn only on the sides with a width, not as a full ring
	[TestMethod]
	public void ABorderIsOnlyOnTheSideItWasAskedFor()
	{
		var pixels = Painted( panel =>
		{
			panel.Style.BackgroundColor = Color.Red;
			Rounded( panel );
			panel.Style.Set( "border-bottom", "2px solid #0000FF" );
		} );

		if ( pixels is null )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		//Along the bottom, in the middle of the panel: red, then the border.
		var inside = Channel( pixels, Size / 2, Size - 12, 2 );
		var band = Channel( pixels, Size / 2, Size - 10, 2 );

		Assert.IsTrue( band > 200, $"the bottom border is not there ({band})" );
		Assert.IsTrue( inside < 40, $"the bottom border reaches too far up ({inside})" );

		//And nowhere near the corner at the other end of the panel.
		for ( var y = 6; y < 16; y++ )
		{
			for ( var x = 6; x < 16; x++ )
			{
				Assert.IsTrue( Channel( pixels, x, y, 2 ) < 40,
					$"a bottom border put colour in the top left corner at {x},{y}" );
			}
		}
	}

	//Gradient angles are in degrees (keywords or deg); radians made every gradient run downwards
	[TestMethod]
	public void AGradientRunsTheWayItWasAsked()
	{
		byte[] Of( string css ) => Painted( panel =>
		{
			panel.Style.BackgroundColor = Color.Transparent;
			panel.Style.Set( "background-image", css );
		} );

		var sideways = Of( "linear-gradient( to right, rgba( 255, 255, 255, 1 ), rgba( 255, 255, 255, 0 ) )" );

		if ( sideways is null )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		var degrees = Of( "linear-gradient( 90deg, rgba( 255, 255, 255, 1 ), rgba( 255, 255, 255, 0 ) )" );
		var down = Of( "linear-gradient( to bottom, rgba( 255, 255, 255, 1 ), rgba( 255, 255, 255, 0 ) )" );

		int Across( byte[] pixels, int x ) => Channel( pixels, x, Size / 2, 0 );
		int Down( byte[] pixels, int y ) => Channel( pixels, Size / 2, y, 0 );

		//Sideways: bright at the left of the panel, gone by the right of it.
		Assert.IsTrue( Across( sideways, 12 ) > 200, $"a sideways gradient does not start bright ({Across( sideways, 12 )})" );
		Assert.IsTrue( Across( sideways, Size - 12 ) < 120, $"a sideways gradient does not fade ({Across( sideways, Size - 12 )})" );

		//The same direction said as an angle is the same picture.
		Assert.AreEqual( Across( sideways, 12 ), Across( degrees, 12 ), 8, "90deg is not to right at the near end" );
		Assert.AreEqual( Across( sideways, Size - 12 ), Across( degrees, Size - 12 ), 8, "90deg is not to right at the far end" );

		//And downwards means downwards: flat across the row, fading down it.
		Assert.AreEqual( Across( down, 12 ), Across( down, Size - 12 ), 8, "a downward gradient changes across the row" );
		Assert.IsTrue( Down( down, 12 ) > 200 && Down( down, Size - 12 ) < 120, "a downward gradient does not fade downwards" );
	}

	//Which way each of the directions a stylesheet can write actually points. No device needed: this is the table itself, and it is the half
	//of the gradient bug that no amount of looking at a screen tells you about - the words and the numbers have to agree, and they did not.
	[TestMethod]
	public void EveryWayOfSayingWhichWayAgrees()
	{
		var styles = new Styles();

		var read = typeof( Styles ).GetMethod( "TryReadGradientDirection",
			BindingFlags.Instance | BindingFlags.NonPublic );

		Assert.IsNotNull( read, "the gradient's direction reader is gone" );

		float? Turn( string text )
		{
			object[] args = { text, 0.0f };

			return (bool)read.Invoke( styles, args ) ? (float)args[1] : null;
		}

		//The turn of a strip that runs downwards: "to bottom" is no turn at all.
		Assert.AreEqual( 0.0f, Turn( "to bottom" ) );
		Assert.AreEqual( 90.0f, Turn( "to right" ) );
		Assert.AreEqual( 180.0f, Turn( "to top" ) );
		Assert.AreEqual( 270.0f, Turn( "to left" ) );

		Assert.AreEqual( 45.0f, Turn( "to bottom right" ) );
		Assert.AreEqual( 135.0f, Turn( "to top right" ) );
		Assert.AreEqual( 225.0f, Turn( "to top left" ) );
		Assert.AreEqual( 315.0f, Turn( "to bottom left" ) );

		//The same directions written as CSS angles, which count from straight up.
		Assert.AreEqual( Turn( "to bottom" ), Turn( "180deg" ) );
		Assert.AreEqual( Turn( "to right" ), Turn( "90deg" ) );
		Assert.AreEqual( Turn( "to top" ), Turn( "0deg" ) );
		Assert.AreEqual( Turn( "to left" ), Turn( "270deg" ) );

		//A colour is not a direction, and neither is a direction that is not one.
		Assert.IsNull( Turn( "rgba( 255, 255, 255, 0.15 )" ) );
		Assert.IsNull( Turn( "#ffffff" ) );
		Assert.IsNull( Turn( "to nowhere" ) );
		Assert.IsNull( Turn( "to top bottom" ) );
	}

	//A panel that draws something of its own over its box.
	private sealed class CustomDrawingPanel : Panel
	{
		public override void OnDraw()
		{
			Panel.Draw.Rect( Box.Rect.Shrink( 8 ), Color.Red );
		}
	}

	//A panel's own drawing reaches the window, not just the screen. The editor's windows are walked by a renderer of their own, and it used to
	//build a panel's box and its text and nothing else - so a panel that drew itself came out empty in every editor window while working on screen.
	[TestMethod]
	public void APanelDrawsItsOwnContent()
	{
		var pixels = Painted( _ => { }, root => root.AddChild<CustomDrawingPanel>() );

		if ( pixels is null )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		//The panel sits at 8,8 and is 48 across; it fills all but 8 of that.
		Assert.IsTrue( Channel( pixels, 32, 32, 0 ) > 200,
			"the middle of what the panel drew is not the colour it drew" );

		//And nothing outside what it asked for.
		Assert.IsTrue( Channel( pixels, 10, 10, 0 ) < 40,
			"the panel drew outside the rectangle it was given" );
	}

	//A texture filled again shows what it was filled with, without being replaced. Panels that draw themselves into a bitmap keep one
	//texture and write over it rather than making a new one every time. That is only worth doing if the walk actually draws what was written.
	[TestMethod]
	public void ATextureFilledAgainIsDrawnAgain()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		using var window = CoreWindow.Create( "rinten texture update", 320, 180, hidden: true );

		if ( window is null || !CoreRenderDevice.Initialize() )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		var view = CoreRenderDevice.CreateView( Size, Size );

		if ( view == 0 )
		{
			Assert.Inconclusive( "nothing to draw with here" );
			return;
		}

		try
		{
			var root = new UiWindowRoot();

			var image = root.AddChild<Rinten.UI.Image>();
			image.Style.Position = PositionMode.Absolute;
			image.Style.Left = 0;
			image.Style.Top = 0;
			image.Style.Width = Size;
			image.Style.Height = Size;
			image.Style.ObjectFit = ObjectFit.Fill;

			using var bitmap = new Bitmap( 8, 8 );

			bitmap.Clear( Color.Red );

			var texture = bitmap.ToTexture( mips: false );
			image.Texture = texture;

			var first = Render( root, view );

			Assert.IsNotNull( first, "the first frame did not come back" );
			Assert.IsTrue( first[( ( 32 * Size ) + 32 ) * 4 + 0] > 200, "the texture was not drawn at all" );

			//The same texture object, filled with something else.
			bitmap.Clear( Color.Green );
			texture.Update( bitmap );

			var second = Render( root, view );

			Assert.IsNotNull( second, "the second frame did not come back" );
			Assert.IsTrue( second[( ( 32 * Size ) + 32 ) * 4 + 1] > 200,
				"a texture filled again was drawn with what it held before" );
		}
		finally
		{
			CoreRenderDevice.DestroyView( view );
		}
	}

	//One frame of a root into a view, read back.
	private static byte[] Render( UiWindowRoot root, ulong view )
	{
		root.PreLayout( new Rect( 0, 0, Size, Size ) );
		root.CalculateLayout();
		root.PostLayout();

		if ( !CoreRenderDevice.BeginViewFrame( view, Color.Black ) ) return null;

		CoreSceneDrawer.DrawUi( root, Size, Size );

		if ( !CoreRenderDevice.EndViewFrame( view ) ) return null;

		var pixels = new byte[Size * Size * 4];

		return CoreRenderDevice.ReadView( view, pixels ) ? pixels : null;
	}
}
