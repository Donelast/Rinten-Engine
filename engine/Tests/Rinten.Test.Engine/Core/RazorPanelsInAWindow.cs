using Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Rinten;
using Rinten.UI;
using System.Collections.Generic;

namespace EngineTests;

//Razor panels in a window with no game, addons or type library, like the launcher
[TestClass]
[DoNotParallelize]
public class RazorPanelsInAWindowTests
{
	//A pointer somebody else is holding, as in the click-path tests.
	private sealed class FakePointer : PanelInput
	{
		public InputData Data;

		internal override InputData GetInputData() => Data;

		public override void SetCursor( string name ) { }

		public void Point( Vector2 at, bool down )
		{
			Data.MousePos = at;
			Data.Mouse0 = down;
		}
	}

	//The shape the launcher is: a row of two, a list built out of a loop, an icon element, and things that answer a click. Written against the builder by
	//hand because that is what the razor compiler writes - the same calls in the same order, so what is tested is the panel system rather than a generator.
	private class MarkupPanel : Panel
	{
		public List<string> Items = new() { "one", "two", "three" };

		public int SideClicks;
		public int ItemClicks;

		protected override string GetRenderTreeChecksum() => "markup";

		protected override int BuildHash() => Items.Count;

		protected override void BuildRenderTree( RenderTreeBuilder builder )
		{
			builder.OpenElement( 0, "root", null );
			{
				builder.OpenElement( 1, "div", null );
				{
					builder.AddAttribute( 2, "class", "side" );

					builder.OpenElement( 3, "div", null );
					{
						builder.AddAttribute( 4, "class", "link" );
						builder.AddAttribute( 5, "onclick", () => SideClicks++ );

						builder.OpenElement( 6, "i", null );
						{
							builder.AddContent( 7, "folder_open" );
						}
						builder.CloseElement();
					}
					builder.CloseElement();
				}
				builder.CloseElement();

				builder.OpenElement( 8, "div", null );
				{
					builder.AddAttribute( 9, "class", "list" );

					var sequence = 10;

					foreach ( var item in Items )
					{
						builder.OpenElement( sequence++, "div", null );
						{
							builder.AddAttribute( sequence++, "class", "card" );
							builder.AddAttribute( sequence++, "onclick", () => ItemClicks++ );
							builder.AddContent( sequence++, item );
						}
						builder.CloseElement();
					}
				}
				builder.CloseElement();
			}
			builder.CloseElement();
		}
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		Rinten.Engine.GlobalContext.Current.UISystem ??= new UISystem();
	}

	//One tick of a window, in the order CoreUiWindow.Frame runs them.
	private static void Frame( RootPanel root, FakePointer pointer, int width = 400, int height = 300 )
	{
		root.TickInternal();

		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();

		root.TickInputInternal();
		pointer.Tick( new[] { root }, mouseIsActive: true );
	}

	private static MarkupPanel Open( out RootPanel root, out FakePointer pointer, string styles = null )
	{
		root = new UiWindowRoot();
		pointer = new FakePointer();

		var panel = new MarkupPanel();

		if ( styles is not null ) panel.StyleSheet.Parse( styles );

		root.AddChild( panel );

		//Settled: a window has been running frames long before anybody clicks, and the markup is built on one tick and laid out on the next.
		for ( var i = 0; i < 4; i++ ) Frame( root, pointer );

		return panel;
	}

	//Every element in the markup is a panel, whether or not a game named it.
	[TestMethod]
	public void MarkupBecomesPanels()
	{
		var panel = Open( out _, out _ );

		var side = panel.Descendants.FirstOrDefault( x => x.HasClass( "side" ) );
		var cards = panel.Descendants.Where( x => x.HasClass( "card" ) ).ToList();

		Assert.IsNotNull( side, "the side of the markup is not in the tree" );
		Assert.AreEqual( 3, cards.Count, "the list built out of a loop is not in the tree" );
	}

	//An icon element is a panel with the icon's name in it - the name is text, and text in the markup is a label under the element that held it.
	[TestMethod]
	public void AnIconElementHoldsItsName()
	{
		var panel = Open( out _, out _ );

		var icon = panel.Descendants.FirstOrDefault( x => x.ElementName == "i" );

		Assert.IsNotNull( icon, "the <i> in the markup is not in the tree" );

		var label = icon.Descendants.OfType<Rinten.UI.Label>().FirstOrDefault();

		Assert.IsNotNull( label, "the icon's name did not become a label" );
		Assert.AreEqual( "folder_open", label.Text );
	}

	//A sheet handed to a panel styles the panel and everything under it.
	[TestMethod]
	public void AStylesheetHandedOverStylesTheMarkup()
	{
		var panel = Open( out var root, out var pointer,
			".list { flex-direction: column; } .card { width: 120px; height: 40px; }" );

		Frame( root, pointer );

		var card = panel.Descendants.First( x => x.HasClass( "card" ) );
		var list = panel.Descendants.First( x => x.HasClass( "list" ) );

		Assert.AreEqual( 120, card.Box.Rect.Width, 0.5f,
			$"card {card.Box.Rect} list {list.Box.Rect} panel {panel.Box.Rect} scale {panel.ScaleToScreen}" );
		Assert.AreEqual( 40, card.Box.Rect.Height, 0.5f );
	}

	//Pressing over something the markup put a handler on runs the handler.
	[TestMethod]
	public void PressingOverMarkupClicksIt()
	{
		//The thing with the handler on it is the row, not the side it sits in: a press
		//has to land on the row itself for its own handler to be the one that runs.
		var panel = Open( out var root, out var pointer,
			".side { width: 100px; height: 300px; } .link { width: 100px; height: 40px; pointer-events: all; }" );

		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), false );
		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), true );
		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), false );
		Frame( root, pointer );

		//The event is queued when the button comes up and run on the tick after, which is a frame a window would have run anyway.
		Frame( root, pointer );

		var link = panel.Descendants.First( x => x.HasClass( "link" ) );

		var side = panel.Descendants.First( x => x.HasClass( "side" ) );

		Assert.AreEqual( 1, panel.SideClicks,
			$"link {link.Box.Rect} visible {link.IsVisible} inside {link.IsInside( new Vector2( 50, 20 ) )} " +
			$"pointer-events {link.ComputedStyle?.PointerEvents} " +
			$"side visible {side.IsVisible} panel visible {panel.IsVisible} root visible {root.IsVisible} " +
			$"hovered [{pointer.Hovered?.Classes}]" );
	}

	//Saying the pointer reaches a panel says it for everything under it. This is what makes an editor panel usable without repeating
	//pointer-events on every row it has - the editor sets it once, on the panel, and the cascade carries it down. See EditorStyles.
	[TestMethod]
	public void ThePointerReachingAPanelReachesWhatIsInIt()
	{
		var panel = Open( out var root, out var pointer,
			"MarkupPanel { pointer-events: all; } " +
			".side { width: 100px; height: 300px; } .link { width: 100px; height: 40px; }" );

		pointer.Point( new Vector2( 50, 20 ), false );
		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), true );
		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), false );
		Frame( root, pointer );
		Frame( root, pointer );

		Assert.AreEqual( 1, panel.SideClicks, "a panel the pointer reaches has rows the pointer reaches" );
	}

	//A subtree that lets the mouse through lets it through all the way down.
	[TestMethod]
	public void APanelThatLetsTheMouseThroughLetsItThroughItsChildren()
	{
		var panel = Open( out var root, out var pointer,
			"MarkupPanel { pointer-events: all; } " +
			".side { width: 100px; height: 300px; pointer-events: none; } " +
			".link { width: 100px; height: 40px; }" );

		pointer.Point( new Vector2( 50, 20 ), false );
		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), true );
		Frame( root, pointer );

		pointer.Point( new Vector2( 50, 20 ), false );
		Frame( root, pointer );
		Frame( root, pointer );

		Assert.AreEqual( 0, panel.SideClicks, "a press went to a row inside a panel that lets the mouse through" );
	}

	//The handler that runs is the one on the thing under the pointer, not the first one the markup wrote.
	[TestMethod]
	public void EachRowInAListAnswersForItself()
	{
		var panel = Open( out var root, out var pointer,
			".side { width: 100px; height: 300px; } " +
			".list { flex-direction: column; width: 300px; } .card { width: 300px; height: 30px; pointer-events: all; }" );

		Frame( root, pointer );

		//Into the second card: past the side, and one row down.
		var at = new Vector2( 150, 45 );

		pointer.Point( at, false );
		Frame( root, pointer );

		pointer.Point( at, true );
		Frame( root, pointer );

		pointer.Point( at, false );
		Frame( root, pointer );
		Frame( root, pointer );

		Assert.AreEqual( 0, panel.SideClicks );
		Assert.AreEqual( 1, panel.ItemClicks );
	}
}
