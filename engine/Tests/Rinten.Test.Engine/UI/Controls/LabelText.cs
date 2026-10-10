using Rinten.UI;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize] //Modifies UI System Global
public class LabelTextTest
{
	bool previousRenderText;

	//Label layout finalization rebuilds the text texture (TextBlock.RebuildTexture), which needs the native render system that this tier
	//never boots. Turn the convar off for the duration of each test - text measurement is pure RichTextKit (CPU) and is unaffected.
	[TestInitialize]
	public void DisableTextTextures()
	{
		previousRenderText = TextBlock.ui_rendertext;
		TextBlock.ui_rendertext = false;
	}

	//Restores the text texture convar so this class doesn't leak state into other tests.
	[TestCleanup]
	public void RestoreTextTextures()
	{
		TextBlock.ui_rendertext = previousRenderText;
	}

	//Creates a root sized 1000x1000 whose children are content-sized in both axes - the
	//default align-items: stretch would otherwise stretch label boxes to fill the cross axis.
	static RootPanel CreateRoot()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
		root.Style.Set( "flex-direction: row; align-items: flex-start;" );
		return root;
	}

	//Text starts null, round-trips through the setter, and assigning null is coalesced to an
	//empty string rather than stored as null. The constructor also adds the "label" class.
	[TestMethod]
	public void TextGetSetRoundTrip()
	{
		var label = new Label();

		Assert.IsTrue( label.Class.Contains( "label" ) );
		Assert.IsNull( label.Text );

		label.Text = "Hello";
		Assert.AreEqual( "Hello", label.Text );

		label.Text = null;
		Assert.AreEqual( "", label.Text );
	}

	//The text+classname constructor sets the text and adds the extra class on top of the built-in "label" class.
	[TestMethod]
	public void ConstructorSetsTextAndClass()
	{
		var label = new Label( "Hi", "greeting" );

		Assert.AreEqual( "Hi", label.Text );
		Assert.IsTrue( label.Class.Contains( "label" ) );
		Assert.IsTrue( label.Class.Contains( "greeting" ) );
	}

	//SetContent (markup inner text) and SetProperty( "text", ... ) (markup attribute) are both equivalent to assigning the Text property directly.
	[TestMethod]
	public void SetContentAndSetPropertyMatchTextProperty()
	{
		var viaProperty = new Label();
		viaProperty.Text = "Hello";

		var viaContent = new Label();
		viaContent.SetContent( "Hello" );

		var viaSetProperty = new Label();
		viaSetProperty.SetProperty( "text", "Hello" );

		Assert.AreEqual( viaProperty.Text, viaContent.Text );
		Assert.AreEqual( viaProperty.Text, viaSetProperty.Text );
	}

	//With Tokenize disabled, text starting with '#' is stored verbatim instead of being run through the language phrase lookup.
	[TestMethod]
	public void TokenizeDisabledKeepsHashPrefixedText()
	{
		var label = new Label { Tokenize = false, Text = "#some.token" };

		Assert.AreEqual( "#some.token", label.Text );
	}

	//TextLength counts text elements (graphemes), not chars - a surrogate-pair emoji is one element even though it is two chars.
	[TestMethod]
	public void TextLengthCountsGraphemes()
	{
		var label = new Label();
		label.Text = "a\U0001F44Db";

		Assert.AreEqual( 4, label.Text.Length );
		Assert.AreEqual( 3, label.TextLength );
	}

	//A label with text and a fixed font size measures to a non-zero, content-sized
	//box after layout - the measure function runs through RichTextKit on the CPU.
	[TestMethod]
	public void MeasuredBoxIsNonZeroAfterLayout()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "Hello";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.IsTrue( label.Box.Rect.Width > 0 );
		Assert.IsTrue( label.Box.Rect.Height > 0 );
		Assert.IsTrue( label.Box.Rect.Width < 1000 );
	}

	//Longer text measures wider than shorter text at the same font size, proving the measured size actually comes from the text content.
	[TestMethod]
	public void LongerTextMeasuresWider()
	{
		var root = CreateRoot();

		var shorter = root.AddChild<Label>();
		shorter.Text = "Hello";
		shorter.Style.Set( "font-size: 16px; white-space: nowrap;" );

		var longer = root.AddChild<Label>();
		longer.Text = "Hello Hello Hello";
		longer.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.IsTrue( shorter.Box.Rect.Width > 0 );
		Assert.IsTrue( longer.Box.Rect.Width > shorter.Box.Rect.Width );
	}

	//At a fixed width, wrapping text (the default) measures taller than the same text with white-space: nowrap, which stays on a single line.
	[TestMethod]
	public void WordWrapIncreasesHeightOverNoWrap()
	{
		const string text = "aa bb cc dd ee ff gg hh";

		var root = CreateRoot();

		var wrapped = root.AddChild<Label>();
		wrapped.Text = text;
		wrapped.Style.Set( "width: 100px; font-size: 16px;" );

		var single = root.AddChild<Label>();
		single.Text = text;
		single.Style.Set( "width: 100px; font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.AreEqual( 100f, wrapped.Box.Rect.Width );
		Assert.IsTrue( single.Box.Rect.Height > 0 );
		Assert.IsTrue( wrapped.Box.Rect.Height > single.Box.Rect.Height );
	}

	//Selection set before the text block exists survives its first layout. Rename fields rely on this to replace the selected name when typing begins.
	[TestMethod]
	public void SelectionSurvivesFirstLayout()
	{
		var root = CreateRoot();
		var label = root.AddChild<Label>();
		label.Text = "Hello";
		label.SelectionStart = 3;
		label.SelectionEnd = 4;
		label.ShouldDrawSelection = true;

		Assert.AreEqual( "l", label.GetSelectedText() );
		root.Layout();

		Assert.AreEqual( 3, label.SelectionStart );
		Assert.AreEqual( 4, label.SelectionEnd );
		Assert.IsTrue( label.ShouldDrawSelection );
		Assert.IsTrue( label.HasSelection() );
		Assert.AreEqual( "l", label.GetSelectedText() );
	}

	//A truncated label must recover its content width when its container grows.
	[TestMethod]
	public void EllipsisRecoversAfterRepeatedResize()
	{
		var root = CreateRoot();
		var label = root.AddChild<Label>();
		label.Text = "A long property name that should become readable again";
		label.Style.Set( "font-size: 16px; min-width: 0; flex-shrink: 1; white-space: nowrap; text-overflow: ellipsis; overflow: hidden;" );
		root.Layout();
		var fullWidth = label.Box.Rect.Width;
		Assert.IsTrue( fullWidth > 100 );

		for ( var cycle = 0; cycle < 3; cycle++ )
		{
			root.PanelBounds = new Rect( 0, 0, 100, 1000 );
			for ( var frame = 0; frame < 3; frame++ ) root.Layout();
			Assert.IsTrue( label.Box.Rect.Width <= 100 );

			root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
			for ( var frame = 0; frame < 3; frame++ ) root.Layout();
			Assert.AreEqual( fullWidth, label.Box.Rect.Width, 1.0f );
		}
	}

	//After layout the selection API works headless: selecting a range reports
	//HasSelection, returns the selected substring and exposes it as the clipboard value.
	[TestMethod]
	public void SelectionWorksAfterLayout()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "Hello World";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		Assert.IsTrue( label.Selectable );

		label.ShouldDrawSelection = true;
		Assert.IsTrue( label.ShouldDrawSelection );

		label.SetSelection( 6, 11 );
		Assert.AreEqual( 6, label.SelectionStart );
		Assert.AreEqual( 11, label.SelectionEnd );
		Assert.IsTrue( label.HasSelection() );
		Assert.AreEqual( "World", label.GetSelectedText() );
		Assert.AreEqual( "World", label.GetClipboardValue( false ) );
	}

	//SetSelection clamps its arguments to the text length, so selecting past the end of the text selects up to the last character.
	[TestMethod]
	public void SelectionEndClampsToTextLength()
	{
		var root = CreateRoot();

		var label = root.AddChild<Label>();
		label.Text = "Hello World";
		label.Style.Set( "font-size: 16px; white-space: nowrap;" );

		root.Layout();

		label.ShouldDrawSelection = true;
		label.SetSelection( 6, 99 );

		Assert.AreEqual( 11, label.SelectionEnd );
		Assert.AreEqual( "World", label.GetSelectedText() );
	}

	//Shrinking the text clamps the caret position back inside the new text bounds.
	[TestMethod]
	public void CaretClampsWhenTextShrinks()
	{
		var label = new Label { Text = "Hello World" };
		label.CaretPosition = 11;

		label.Text = "Hi";

		Assert.AreEqual( 2, label.CaretPosition );
	}

	//The editing helpers InsertText and RemoveText rewrite Text by text-element positions without needing any layout.
	[TestMethod]
	public void InsertAndRemoveTextEditsContent()
	{
		var label = new Label { Text = "Hello" };

		label.InsertText( " World", 5 );
		Assert.AreEqual( "Hello World", label.Text );

		label.RemoveText( 0, 6 );
		Assert.AreEqual( "World", label.Text );
		Assert.AreEqual( 5, label.TextLength );
	}
	//In a row that aligns on baselines, labels of different sizes line their first lines' baselines up rather than their
	//tops or bottoms: the smaller sits lower by the difference in baselines, which the label reports from its text block.
	[TestMethod]
	public void BaselineAlignmentLinesTextUp()
	{
		var root = CreateRoot();
		root.Style.Set( "flex-direction: row; align-items: baseline;" );

		var big = root.AddChild<Label>();
		big.Text = "Big";
		big.Style.Set( "font-size: 32px; white-space: nowrap;" );

		var small = root.AddChild<Label>();
		small.Text = "small";
		small.Style.Set( "font-size: 12px; white-space: nowrap;" );

		root.Layout();

		var bigBaseline = big._textBlock.FirstBaseline( big.Box.Rect.Width, big.Box.Rect.Height );
		var smallBaseline = small._textBlock.FirstBaseline( small.Box.Rect.Width, small.Box.Rect.Height );

		Assert.IsTrue( bigBaseline > smallBaseline, "the larger text has the lower baseline" );
		Assert.IsTrue( small.Box.Rect.Top > big.Box.Rect.Top, "the smaller text sits lower" );
		Assert.AreEqual( big.Box.Rect.Top + bigBaseline, small.Box.Rect.Top + smallBaseline, 1.0f, "the baselines meet" );
	}
}
