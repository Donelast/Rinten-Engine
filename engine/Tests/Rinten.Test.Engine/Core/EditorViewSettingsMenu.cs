using Editor;
using Rinten;
using Rinten.UI;
using System.Linq;

namespace EngineTests;

//View Settings and other menus that mix widgets, headings, and options.
[TestClass]
[DoNotParallelize]
public class EditorViewSettingsMenuTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;
	}

	[TestMethod]
	public void AHeadingIsARowBelowWidgetsNotAnotherWidgetAtTheTop()
	{
		var menu = new Menu();

		var sheet = new Widget( menu );
		sheet.MinimumHeight = 48;

		menu.AddWidget( sheet );
		menu.AddSeparator();
		menu.AddHeading( "Lighting" );
		menu.AddOption( "Lit", null, () => { } );
		menu.Build();

		Assert.AreEqual( 1, menu.Widgets.Count, "only the sheet is an embedded widget" );

		var rows = menu.Rows.OrderBy( x => x.Top ).ToArray();

		Assert.AreEqual( 3, rows.Length, "separator, heading, and option are rows" );
		Assert.IsTrue( rows[0].Top > sheet.Position.y,
			"rows start below the embedded sheet" );
		Assert.IsTrue( rows[1].Top > rows[0].Top,
			"heading sits under the separator" );
		Assert.IsTrue( rows[2].Top > rows[1].Top,
			"options sit under the heading" );

		menu.Destroy();
	}

	[TestMethod]
	public void AnOpenMenuOnTheOverlayDoesNotAddItsOwnersPositionToScreenCoords()
	{
		var owner = new Widget( null );
		owner.Size = new Vector2( 400, 300 );
		owner.Position = new Vector2( 100, 80 );

		var menu = new Menu( owner );

		var sheet = new Widget( menu );
		menu.AddWidget( sheet );
		menu.Build();

		menu.Owner = owner;
		menu.SetPanelParent( null );
		menu.Position = new Vector2( 200, 150 );
		sheet.Position = new Vector2( Menu.Padding, Menu.Padding );
		sheet.Size = new Vector2( 200, 40 );

		var control = new Widget( sheet ) { Position = new Vector2( 12, 8 ) };

		Assert.AreEqual( 200 + Menu.Padding + 12, control.ScreenPosition.x, 0.5f );
		Assert.AreEqual( 150 + Menu.Padding + 8, control.ScreenPosition.y, 0.5f );

		menu.Destroy();
		owner.Destroy();
	}

	[TestMethod]
	public void ACheckableOptionReadsFetchCheckedStateWhenTheMenuRepaints()
	{
		var menu = new Menu();

		var current = 0;

		var first = menu.AddOption( "One", null, () => current = 0 );
		first.Checkable = true;
		first.FetchCheckedState = () => current == 0;

		var second = menu.AddOption( "Two", null, () => current = 1 );
		second.Checkable = true;
		second.FetchCheckedState = () => current == 1;

		menu.Build();

		var rows = menu.Rows.OrderBy( x => x.Top ).ToArray();

		Assert.AreEqual( 2, rows.Length );

		current = 1;
		menu.Update();

		Assert.IsFalse( first.Checked );
		Assert.IsTrue( second.Checked );

		menu.Destroy();
	}

	[TestMethod]
	public void PressingInsideAPopupOnAMenuWidgetDoesNotCloseTheMenu()
	{
		var root = new RootPanel();
		var owner = new Widget( null );
		owner.Size = new Vector2( 400, 300 );
		root.AddChild( owner.Surface );

		EditorOverlay.For( root );

		var menu = new Menu( owner );
		var sheet = new Widget( menu );
		sheet.MinimumHeight = 32;
		menu.AddWidget( sheet );
		menu.Build();

		menu.OpenAt( new Vector2( 80, 60 ), modal: false );

		Assert.IsTrue( menu.IsOpen );

		var popup = new PopupWidget( sheet );
		var inside = new Widget( popup );
		popup.Layout = Layout.Column();
		popup.Layout.Add( inside );
		popup.Show();

		Menu.PressedOn( inside );

		Assert.IsTrue( menu.IsOpen, "the view settings menu stays open while picking from a popup on it" );

		popup.Destroy();
		menu.Destroy();
		owner.Destroy();
		root.Delete();
	}
}
