using System;
using System.Globalization;
using System.Text.Json;
using System.Threading;

namespace SystemTests;

//Number formatting and parsing is culture-invariant; tests force a comma-decimal culture
[TestClass]
[DoNotParallelize]
public class InvariantNumbersTest
{
	//Do something on a machine that writes a decimal comma.
	private static void WithACommaForAPoint( Action what )
	{
		var was = Thread.CurrentThread.CurrentCulture;

		try
		{
			Thread.CurrentThread.CurrentCulture = new CultureInfo( "ru-RU" );

			//The premise of all of this, so it is worth failing loudly if the machine has no such culture at all.
			Assert.AreEqual( ",", CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator );

			what();
		}
		finally
		{
			Thread.CurrentThread.CurrentCulture = was;
		}
	}

	[TestMethod]
	public void NumbersInStringsAreReadWithAPoint()
	{
		WithACommaForAPoint( () =>
		{
			Assert.AreEqual( 3.5f, "3.5".ToFloat(), "a style sheet says 3.5 wherever it is read" );
			Assert.AreEqual( 3.5f, "3.5".ToDouble() );
			Assert.AreEqual( 3.5m, "3.5".ToDecimal() );

			Assert.AreEqual( 42, "42".ToInt() );
			Assert.AreEqual( 42L, "42".ToLong() );
			Assert.AreEqual( 42ul, "42".ToULong() );

			Assert.IsTrue( "0.5".ToBool(), "and something that is not zero is true" );
		} );
	}

	[TestMethod]
	public void AColourSurvivesBeingSavedOnSuchAMachine()
	{
		var colour = new Color( 0.5f, 0.25f, 0.125f, 1.0f );

		string json = null;

		WithACommaForAPoint( () => json = JsonSerializer.Serialize( colour ) );

		//Written with points, so the numbers are four and not seven.
		Assert.IsFalse( json.Contains( "0,5" ), $"a comma is a separator here, not a decimal point - {json}" );

		//And read back both by that machine and by anybody else's.
		WithACommaForAPoint( () => Assert.AreEqual( colour, JsonSerializer.Deserialize<Color>( json ) ) );

		Assert.AreEqual( colour, JsonSerializer.Deserialize<Color>( json ) );
	}

	[TestMethod]
	public void SoDoThePositionsAndTheRotations()
	{
		var position = new Vector3( 1.5f, -2.25f, 0.125f );
		var rotation = Rotation.From( 30.5f, 12.25f, 0.0f );

		WithACommaForAPoint( () =>
		{
			Assert.AreEqual( position, JsonSerializer.Deserialize<Vector3>( JsonSerializer.Serialize( position ) ) );

			var back = JsonSerializer.Deserialize<Rotation>( JsonSerializer.Serialize( rotation ) );

			Assert.AreEqual( rotation.x, back.x, 0.0001f );
			Assert.AreEqual( rotation.y, back.y, 0.0001f );
			Assert.AreEqual( rotation.z, back.z, 0.0001f );
			Assert.AreEqual( rotation.w, back.w, 0.0001f );
		} );
	}

	//And what a colour prints as is what it parses from, which is the same question one step away from the file.
	[TestMethod]
	public void AColourReadsBackWhatItPrints()
	{
		var colour = new Color( 0.5f, 0.25f, 0.125f, 1.0f );

		WithACommaForAPoint( () =>
		{
			var printed = colour.ToString();

			Assert.IsTrue( Color.TryParse( printed, out var back ), $"should have read {printed}" );

			Assert.AreEqual( colour.r, back.r, 0.01f );
			Assert.AreEqual( colour.g, back.g, 0.01f );
			Assert.AreEqual( colour.b, back.b, 0.01f );
			Assert.AreEqual( colour.a, back.a, 0.01f );
		} );
	}
}
