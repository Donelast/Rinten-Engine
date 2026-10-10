using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//Who the keyboard belongs to while somebody is writing. The editor's shortcuts hear every key, because what is held down is a question anything
//may ask later. That is right over a viewport and wrong in a field: renaming something to "grid" should not toggle four settings on the way past.
[TestClass]
[DoNotParallelize]
public class EditorShortcutFocusTests
{
	//A widget that says the keys are its own - the console does this.
	private class Raw : Widget
	{
		public Raw() : base( null ) { }

		public override bool WantsRawKeys => true;
	}

	//A control written as markup, which is not a widget at all.
	private class Field : Panel, IWantsRawKeys
	{
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	[TestMethod]
	public void WithNothingFocusedEveryKeyIsTheWindowsOwn()
	{
		Assert.IsFalse( EditorShortcuts.Suppressed( null, null, "G" ) );
		Assert.IsFalse( EditorShortcuts.Suppressed( null, null, "CTRL+S" ) );
	}

	[TestMethod]
	public void AWidgetThatWantsRawKeysKeepsTheLetters()
	{
		var widget = new Raw();

		Assert.IsTrue( EditorShortcuts.Suppressed( widget, null, "G" ) );
		Assert.IsTrue( EditorShortcuts.Suppressed( widget, null, "SHIFT+G" ), "shift is how capitals are typed" );
	}

	[TestMethod]
	public void TheWindowKeepsItsControlCombinations()
	{
		var widget = new Raw();

		Assert.IsFalse( EditorShortcuts.Suppressed( widget, null, "CTRL+S" ) );
		Assert.IsFalse( EditorShortcuts.Suppressed( widget, null, "ALT+ENTER" ) );
	}

	[TestMethod]
	public void TheFourAFieldUsesItselfStayWithTheField()
	{
		var widget = new Raw();

		foreach ( var keys in new[] { "CTRL+A", "CTRL+C", "CTRL+V", "CTRL+X" } )
		{
			Assert.IsTrue( EditorShortcuts.Suppressed( widget, null, keys ), $"{keys} belongs to what is being typed in" );
		}
	}

	[TestMethod]
	public void AControlWrittenAsMarkupIsAskedToo()
	{
		var field = new Field();

		Assert.IsTrue( EditorShortcuts.Suppressed( null, field, "G" ) );
		Assert.IsFalse( EditorShortcuts.Suppressed( null, field, "CTRL+S" ) );
	}

	[TestMethod]
	public void WhatHoldsTheFocusIsUsuallyInsideTheField()
	{
		//The focus lands on the label doing the editing rather than on the field around it, so everything it is inside is asked.
		var field = new Field();
		var inside = new Panel();

		field.AddChild( inside );

		Assert.IsTrue( EditorShortcuts.Suppressed( null, inside, "G" ) );
	}

	[TestMethod]
	public void APlainPanelDoesNotHoldOntoTheKeyboard()
	{
		Assert.IsFalse( EditorShortcuts.Suppressed( null, new Panel(), "G" ) );
	}
}
