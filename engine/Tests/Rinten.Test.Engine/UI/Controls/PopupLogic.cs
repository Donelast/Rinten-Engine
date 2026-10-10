using Rinten.UI;
using System.Collections.Generic;
using System.Reflection;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] //Modifies UI System Global (static popup registry)
public class PopupLogicTest
{
	//Minimal concrete popup - BasePopup is abstract and supplies all of the behavior under test.
	sealed class ProbePopup : BasePopup
	{
	}

	//The private static popup registry inside BasePopup, fetched via reflection so the tests can assert real registration state rather than inferring it.
	static List<BasePopup> Registry => (List<BasePopup>)typeof( BasePopup )
		.GetField( "AllPopups", BindingFlags.NonPublic | BindingFlags.Static )
		.GetValue( null );

	//The popup registry is static engine state, so delete and drop anything left behind by
	//other tests to make membership assertions only see popups created by the current test.
	[TestInitialize]
	public void ResetPopupRegistry()
	{
		foreach ( var popup in Registry.ToArray() )
		{
			popup.Delete( true );
		}

		Registry.Clear();
	}

	//Constructing a popup registers it in the static popup registry.
	[TestMethod]
	public void ConstructingPopupRegistersIt()
	{
		var popup = new ProbePopup();

		Assert.AreEqual( 1, Registry.Count );
		Assert.IsTrue( Registry.Contains( popup ) );
	}

	//CloseAll deletes an open popup and unregisters it - the delete is the deferred
	//(outro) variant, so the popup is flagged IsDeleting rather than destroyed on the spot.
	[TestMethod]
	public void CloseAllDeletesOpenPopups()
	{
		var root = new RootPanel();
		var popup = new ProbePopup { Parent = root };

		BasePopup.CloseAll();

		Assert.IsFalse( Registry.Contains( popup ) );
		Assert.IsTrue( popup.IsDeleting );
	}

	//A StayOpen popup with a valid parent survives CloseAll while its non-StayOpen sibling is closed and unregistered.
	[TestMethod]
	public void CloseAllRespectsStayOpenWithValidParent()
	{
		var root = new RootPanel();
		var pinned = new ProbePopup { Parent = root, StayOpen = true };
		var transient = new ProbePopup { Parent = root };

		BasePopup.CloseAll();

		Assert.IsTrue( Registry.Contains( pinned ) );
		Assert.IsFalse( pinned.IsDeleting );

		Assert.IsFalse( Registry.Contains( transient ) );
		Assert.IsTrue( transient.IsDeleting );
	}

	//StayOpen is only honored when the popup still has a valid parent - an orphaned StayOpen popup is deleted by CloseAll like any other.
	[TestMethod]
	public void CloseAllDeletesOrphanedStayOpenPopups()
	{
		var orphan = new ProbePopup { StayOpen = true };

		BasePopup.CloseAll();

		Assert.IsFalse( Registry.Contains( orphan ) );
		Assert.IsTrue( orphan.IsDeleting );
	}

	//Passing a panel to CloseAll spares the popup found in that panel's ancestry, so a popup is not closed by clicks/interactions inside its own hierarchy.
	[TestMethod]
	public void CloseAllSparesTheExceptedPopupHierarchy()
	{
		var root = new RootPanel();
		var popup = new ProbePopup { Parent = root };
		var inner = new Panel { Parent = popup };

		BasePopup.CloseAll( inner );

		Assert.IsTrue( Registry.Contains( popup ) );
		Assert.IsFalse( popup.IsDeleting );
	}

	//CloseAll spares a popup when the click lands on its anchor panel.
	[TestMethod]
	public void CloseAllSparesPopupWhenClickIsOnAnchorPanel()
	{
		var root = new RootPanel();
		var source = new Panel { Parent = root };
		var popup = new ProbePopup { Parent = root, AnchorPanel = source };

		BasePopup.CloseAll( source );

		Assert.IsTrue( Registry.Contains( popup ) );
		Assert.IsFalse( popup.IsDeleting );
	}

	//A popup opened from inside another popup should not dismiss the host on click.
	[TestMethod]
	public void CloseAllSparesHostPopupWhenClickingNestedPopup()
	{
		var root = new RootPanel();
		var host = new ProbePopup { Parent = root };
		var anchor = new Panel { Parent = host };
		var nested = new ProbePopup { Parent = root, AnchorPanel = anchor, OpenedFrom = host };
		var option = new Panel { Parent = nested };

		BasePopup.CloseAll( option );

		Assert.IsTrue( Registry.Contains( host ) );
		Assert.IsFalse( host.IsDeleting );
		Assert.IsTrue( Registry.Contains( nested ) );
		Assert.IsFalse( nested.IsDeleting );
	}

	//Deleting a popup directly (without going through CloseAll) also removes it from the registry via OnDeleted.
	[TestMethod]
	public void ImmediateDeleteUnregistersPopup()
	{
		var root = new RootPanel();
		var popup = new ProbePopup { Parent = root };

		Assert.IsTrue( Registry.Contains( popup ) );

		popup.Delete( true );

		Assert.IsFalse( Registry.Contains( popup ) );
	}
}
