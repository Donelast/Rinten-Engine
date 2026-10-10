using Editor;

namespace EngineTests;

//The window overlay layer for menus and tooltips must keep floating things inside the window
[TestClass]
public class EditorOverlayLayerTests
{
	private static readonly Vector2 Window = new( 1000, 800 );

	[TestMethod]
	public void SomethingThatFitsIsLeftWhereItAskedToBe()
	{
		var at = EditorOverlay.Place( new Vector2( 120, 90 ), new Vector2( 200, 300 ), Window );

		Assert.AreEqual( new Vector2( 120, 90 ), at );
	}

	[TestMethod]
	public void SomethingHangingOffTheRightIsPulledBackIn()
	{
		var at = EditorOverlay.Place( new Vector2( 950, 90 ), new Vector2( 200, 300 ), Window );

		Assert.AreEqual( 800, at.x, "it should sit against the right edge, not past it" );
		Assert.AreEqual( 90, at.y );
	}

	[TestMethod]
	public void SomethingHangingOffTheBottomIsPulledBackIn()
	{
		var at = EditorOverlay.Place( new Vector2( 100, 700 ), new Vector2( 200, 300 ), Window );

		Assert.AreEqual( 100, at.x );
		Assert.AreEqual( 500, at.y );
	}

	[TestMethod]
	public void SomethingTooBigToFitStartsAtTheCorner()
	{
		//The half people read first is the top left one, so that is the half that is kept rather than the other edge.
		var at = EditorOverlay.Place( new Vector2( 400, 400 ), new Vector2( 1400, 1200 ), Window );

		Assert.AreEqual( Vector2.Zero, at );
	}

	[TestMethod]
	public void SomethingAskedForOffTheTopLeftIsBroughtBack()
	{
		var at = EditorOverlay.Place( new Vector2( -50, -20 ), new Vector2( 200, 300 ), Window );

		Assert.AreEqual( Vector2.Zero, at );
	}

	//Tooltips. Half of the editor's are written as a scrap of markup, because the toolkit that used to draw them read it. A label here does not.

	[TestMethod]
	public void APlainTooltipIsLeftAlone()
	{
		Assert.AreEqual( "Toggle Fullscreen", EditorOverlay.PlainText( "Toggle Fullscreen" ) );
	}

	[TestMethod]
	public void ATooltipWrittenAsMarkupKeepsItsWordsAndItsLineBreak()
	{
		Assert.AreEqual( "Select\nPick things in the scene",
			EditorOverlay.PlainText( "<b>Select</b><br>Pick things in the scene" ) );
	}

	[TestMethod]
	public void ABracketThatOpensNothingIsABracketSomebodyMeant()
	{
		Assert.AreEqual( "a < b", EditorOverlay.PlainText( "a < b" ) );
	}

	[TestMethod]
	public void NothingStaysNothing()
	{
		Assert.AreEqual( string.Empty, EditorOverlay.PlainText( string.Empty ) );
		Assert.IsNull( EditorOverlay.PlainText( null ) );
	}
}
