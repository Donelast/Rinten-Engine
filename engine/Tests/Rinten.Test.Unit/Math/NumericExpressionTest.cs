namespace MathTests;

[TestClass]
public class NumericExpressionTest
{
	[TestMethod]
	[DataRow( "96*2", 192.0 )]
	[DataRow( "96 * 2", 192.0 )]
	[DataRow( "1+2*3", 7.0 )]
	[DataRow( "(1+2)*3", 9.0 )]
	[DataRow( "10/4", 2.5 )]
	[DataRow( "10-2-3", 5.0 )]
	[DataRow( "7%4", 3.0 )]
	[DataRow( "2^8", 256.0 )]
	[DataRow( "2^3^2", 512.0 )]
	[DataRow( "-2^2", -4.0 )]
	[DataRow( "-5", -5.0 )]
	[DataRow( "--5", 5.0 )]
	[DataRow( "3*-2", -6.0 )]
	[DataRow( "1.5*2", 3.0 )]
	[DataRow( "((4))", 4.0 )]
	[DataRow( "12.25", 12.25 )]
	public void Evaluates( string text, double expected )
	{
		Assert.IsTrue( NumericExpression.TryEvaluate( text, out var value ), text );
		Assert.AreEqual( expected, value, 1e-9, text );
	}

	//What is still being typed, or never was a number, comes to nothing rather than to zero.
	[TestMethod]
	[DataRow( "" )]
	[DataRow( " " )]
	[DataRow( "-" )]
	[DataRow( "96*" )]
	[DataRow( "(1+2" )]
	[DataRow( "1+2)" )]
	[DataRow( "*2" )]
	[DataRow( "1/0" )]
	[DataRow( "2 3" )]
	public void RejectsIncomplete( string text )
	{
		Assert.IsFalse( NumericExpression.TryEvaluate( text, out _ ), text );
	}
}
