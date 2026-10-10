using Rinten;
using Rinten.UI;
using System.Linq;

namespace EngineTests;

//The bar along the top of a window of ours.
[TestClass]
[DoNotParallelize]
public class WindowCaptionTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		Rinten.Engine.GlobalContext.Current.UISystem ??= new UISystem();
	}

	private static WindowCaption Layout( string heading = "rinten", int width = 800 )
	{
		var root = new UiWindowRoot();

		root.Style.FlexDirection = FlexDirection.Column;
		root.Style.Width = Length.Percent( 100 );
		root.Style.Height = Length.Percent( 100 );

		var bar = WindowCaption.Attach( null, root, heading );

		root.PreLayout( new Rect( 0, 0, width, 600 ) );
		root.CalculateLayout();
		root.PostLayout();

		return bar;
	}

	[TestMethod]
	public void TheBarHasTheThreeWindowButtons()
	{
		var bar = Layout();

		var controls = bar.Descendants.Where( x => x.HasClass( "control" ) ).ToList();

		Assert.AreEqual( 3, controls.Count, "minimize, maximize, close" );

		Assert.IsTrue( controls[^1].HasClass( "shut" ), "the last one is the one that closes the window" );
	}

	[TestMethod]
	public void TheBarSaysWhatTheWindowIsCalled()
	{
		var bar = Layout( "Welcome to the Rinten editor" );

		Assert.AreEqual( "Welcome to the Rinten editor", bar.Title );
	}

	[TestMethod]
	public void TheBarIsTheHeightOfACaption()
	{
		var bar = Layout();

		Assert.AreEqual( WindowCaption.BarHeight, bar.Box.Rect.Height, 0.5f );
		Assert.AreEqual( 800, bar.Box.Rect.Width, 0.5f, "it spans the window" );
	}

	[TestMethod]
	public void TheIconsAskForTheIconFont()
	{
		var bar = Layout();

		var icons = bar.Descendants.Where( x => x.ElementName == "i" ).ToList();

		Assert.AreEqual( 3, icons.Count );

		foreach ( var icon in icons )
		{
			Assert.AreEqual( "Material Icons", icon.ComputedStyle?.FontFamily,
				$"an icon is not asking for the icon font" );
		}
	}

	[TestMethod]
	public void WhatIsInTheWindowSitsUnderTheBar()
	{
		var root = new UiWindowRoot();

		root.Style.FlexDirection = FlexDirection.Column;
		root.AddClass( "captioned" );

		var bar = WindowCaption.Attach( null, root, "rinten" );

		var body = root.AddChild<Panel>( "window-body" );
		body.Style.FlexGrow = 1;
		body.Style.FlexShrink = 1;
		body.Style.Width = Length.Percent( 100 );
		body.Style.MinHeight = 0;

		var filler = body.AddChild<Panel>();
		filler.Style.FlexGrow = 1;
		filler.Style.Width = Length.Percent( 100 );
		filler.Style.Height = Length.Percent( 100 );

		root.PreLayout( new Rect( 0, 0, 800, 600 ) );
		root.CalculateLayout();
		root.PostLayout();

		Assert.AreEqual( 0, bar.Box.Rect.Top, 0.5f );
		Assert.AreEqual( WindowCaption.BarHeight, bar.Box.Rect.Height, 0.5f );
		Assert.IsTrue( body.Box.Rect.Top >= WindowCaption.BarHeight - 0.5f,
			$"the body starts at {body.Box.Rect.Top}, on top of the bar" );
		Assert.IsTrue( filler.Box.Rect.Top >= WindowCaption.BarHeight - 0.5f,
			$"the content starts at {filler.Box.Rect.Top}, on top of the bar" );
		Assert.IsTrue( body.Box.Rect.Height > 100, "the body fills what the bar left" );
	}
}
