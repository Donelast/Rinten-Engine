using Rinten.UI;

namespace UITests.Parsers;

[TestClass]
public class StripCommentsTest
{
	[TestMethod]
	public void SingleLine()
	{
		var str = StyleParser.StripComments( "One\nTwo// hello\nThree" );
		Assert.AreEqual( "One\nTwo\nThree", str );
	}

	[TestMethod]
	public void CommentsInsideComments()
	{
		var str = StyleParser.StripComments( "One\nTwo// hello // more // bullshit /*\nThree" );
		Assert.AreEqual( "One\nTwo\nThree", str );

		str = StyleParser.StripComments( "One\nTwo/* hello /* more // bullshit */\nThree" );
		Assert.AreEqual( "One\nTwo\nThree", str );
	}

	[TestMethod]
	public void Multiline()
	{
		//The comment goes and its newlines stay: what is left has as many lines as went in, so a line reported out
		//of the stripped text is a line of the file. StyleSheetLinesTest, in Rinten.Test.Unit, is what that is for.
		var str = StyleParser.StripComments( "One\nTwo /* hello\n   Hello*/Three" );
		Assert.AreEqual( "One\nTwo \nThree", str );
	}

	[TestMethod]
	public void DontThinkUrlsAreComments()
	{
		var str = "bloop\nbackground-image: url( https://hello );\nfloop";

		var stripped = StyleParser.StripComments( str );
		Assert.AreEqual( str, stripped );
	}
}
