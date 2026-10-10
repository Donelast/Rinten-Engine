using Rinten.UI;

namespace UITests;

//A rule's line number must be a line of the source file, so errors and tools point at it
[TestClass]
public class StyleSheetLinesTest
{
	//The line a block reports is its first declaration's, counted from 0.
	[TestMethod]
	public void BlockKnowsItsLine()
	{
		//0 .a  1 {  2 color  3 }  4 blank  5 .b  6 {  7 color
		var sheet = StyleParser.ParseSheet( ".a\n{\n\tcolor: red;\n}\n\n.b\n{\n\tcolor: blue;\n}" );

		Assert.AreEqual( 2, sheet.Nodes.Count );
		Assert.AreEqual( 2, sheet.Nodes[0].FileLine );
		Assert.AreEqual( 7, sheet.Nodes[1].FileLine );
	}

	//Comments are taken out before a sheet is read, and used to take their newlines with them:
	//everything under a block comment then reported a line too high for every line of the comment.
	[TestMethod]
	public void BlockCommentDoesNotMoveTheLinesUnderIt()
	{
		//0 /* one  1 two  2 three */  3 .a  4 {  5 color
		var sheet = StyleParser.ParseSheet( "/* one\ntwo\nthree */\n.a\n{\n\tcolor: red;\n}" );

		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 5, sheet.Nodes[0].FileLine );
	}

	//A comment between two rules moves neither of them.
	[TestMethod]
	public void BlockCommentBetweenRules()
	{
		//0 .a  1 {  2 color  3 }  4 /*  5 mid  6 */  7 .b  8 {  9 color
		var sheet = StyleParser.ParseSheet( ".a\n{\n\tcolor: red;\n}\n/*\nmid\n*/\n.b\n{\n\tcolor: blue;\n}" );

		Assert.AreEqual( 2, sheet.Nodes.Count );
		Assert.AreEqual( 2, sheet.Nodes[0].FileLine );
		Assert.AreEqual( 9, sheet.Nodes[1].FileLine );
	}

	//Stripping comments leaves as many lines as it was given.
	[TestMethod]
	public void StrippingCommentsKeepsTheLineCount()
	{
		foreach ( var text in new[]
		{
			"One\nTwo /* hello\n   Hello*/Three",
			".a { color: red; }\n/* one\ntwo\nthree */\n.b { color: blue; }",
			"/*\n\n\n*/.a { color: red; }",
			".a { color: red; }\n// gone\n.b { color: blue; }",
			".a /* mid\nselector */ .b { color: red; }",
		} )
		{
			var stripped = StyleParser.StripComments( text );

			Assert.AreEqual( Lines( text ), Lines( stripped ), $"line count changed for [{text}]" );
		}
	}

	//A sheet taken out of a larger file - the css in a razor's <style> - reports the lines it has in that file.
	[TestMethod]
	public void LineOffsetPutsTheSheetWhereItWasTakenFrom()
	{
		const string css = ".a\n{\n\tcolor: red;\n}";

		var alone = StyleParser.ParseSheet( css );
		var moved = StyleParser.ParseSheet( css, "Panel.razor", lineOffset: 10 );

		Assert.AreEqual( 2, alone.Nodes[0].FileLine );
		Assert.AreEqual( 12, moved.Nodes[0].FileLine );
	}

	//What a <style> in a razor comes to. The compiler hands over the tag's text starting with the break after
	//> and says which line the tag is on, counted from 1 - see PanelRenderTreeBuilder.AddStyleDefinitions .
	[TestMethod]
	public void StyleTagInRazorReportsRazorLines()
	{
		//A razor whose <style> tag is on line 4, so the sheet below starts on line 5
		var styles = "\n    .a\n    {\n        color: red;\n    }\n";

		var sheet = StyleParser.ParseSheet( styles, "Panel.razor", lineOffset: 4 - 1 );

		Assert.AreEqual( 1, sheet.Nodes.Count );

		//The declaration is on line 7 counted from 1, which is 6 from 0.
		Assert.AreEqual( 6, sheet.Nodes[0].FileLine );
	}

	//An imported sheet is a file of its own, wherever the @import was written.
	[TestMethod]
	public void ImportIsNotMovedByTheOffset()
	{
		//Nothing is mounted here, so the import fails and the rule after it still has to land where it is written.
		var sheet = StyleParser.ParseSheet(
			"@import \"nothing.scss\";\n.a\n{\n\tcolor: red;\n}",
			"Panel.razor",
			recover: true,
			lineOffset: 4 );

		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 3 + 4, sheet.Nodes[0].FileLine );
	}

	static int Lines( string text )
	{
		var count = 0;

		for ( var i = 0; i < text.Length; i++ )
		{
			if ( text[i] == '\n' ) count++;
		}

		return count;
	}
}
