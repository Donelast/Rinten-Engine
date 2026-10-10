using Editor;
using NativeEngine;
using Rinten;

namespace EngineTests;

//Shortcut spellings (DEL, ESC, KP_ADD) match pressed key names (DELETE, ESCAPE, PAD_PLUS)
[TestClass]
[DoNotParallelize]
public class EditorShortcutKeyTests
{
	//What a press of this button gives the shortcut system.
	private static string Pressed( ButtonCode button ) => EditorShortcuts.Canonical( EngineInput.CodeToString( button ) );

	//What a shortcut written like this is held as.
	private static string Written( string keys ) => EditorShortcuts.Canonical( keys );

	[TestMethod]
	public void TheDeleteKeyIsTheKeyDeleteShortcutsAreWrittenWith()
	{
		Assert.AreEqual( Pressed( ButtonCode.KEY_DELETE ), Written( "DEL" ) );
	}

	[TestMethod]
	public void TheKeysTheEditorsOwnShortcutsAreWrittenWith()
	{
		//Every spelling the editor actually declares a shortcut with, against the button a keyboard sends for it.
		Assert.AreEqual( Pressed( ButtonCode.KEY_ESCAPE ), Written( "ESC" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_ENTER ), Written( "enter" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_BACKSPACE ), Written( "Backspace" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_SPACE ), Written( "SPACE" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_PAD_PLUS ), Written( "KP_ADD" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_PAD_MINUS ), Written( "KP_MINUS" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_LBRACKET ), Written( "[" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_RBRACKET ), Written( "]" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_F2 ), Written( "F2" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_G ), Written( "G" ) );
	}

	[TestMethod]
	public void AHeldModifierIsTheSameKeyWhichever()
	{
		//A bind says SHIFT, and a keyboard has two of them.
		Assert.AreEqual( Pressed( ButtonCode.KEY_LSHIFT ), Written( "SHIFT" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_RSHIFT ), Written( "SHIFT" ) );
		Assert.AreEqual( Pressed( ButtonCode.KEY_RCONTROL ), Written( "CTRL" ) );
	}

	[TestMethod]
	public void ACombinationIsCanonicalKeyByKey()
	{
		Assert.AreEqual( "CTRL+S", Written( "ctrl+s" ) );
		Assert.AreEqual( $"CTRL+{Pressed( ButtonCode.KEY_PAD_3 )}", Written( "CTRL+KP_3" ) );
		Assert.AreEqual( $"ALT+{Pressed( ButtonCode.KEY_UP )}", Written( "ALT+UP" ) );
	}

	[TestMethod]
	public void AKeyNothingAnswersToIsLeftAsItWasWritten()
	{
		//Better a shortcut nobody can press than one that reads as blank in the list of them.
		Assert.AreEqual( "MADEUPKEY", Written( "madeupkey" ) );
	}

	[TestMethod]
	public void NothingIsNothing()
	{
		Assert.AreEqual( string.Empty, Written( null ) );
		Assert.AreEqual( string.Empty, Written( "  " ) );
	}
}
