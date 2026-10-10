using System;

namespace EngineTests;

//Window ground colour limits: windows are mostly opaque, so the ground must stay dark
[TestClass]
public class EditorGroundTest
{
	//Ground below 1/12 brightness, so the blurred desktop still shows through the 96% opaque window
	[TestMethod]
	public void TheGroundIsDarkEnoughForTheBlurToShowInIt()
	{
		var ground = Rinten.UI.EditorTokens.Colour( "ground" );

		Assert.AreEqual( 1.0f, ground.a, "the ground has to be opaque - there is nothing behind a window" );

		var brightest = MathF.Max( ground.r, MathF.Max( ground.g, ground.b ) );

		Assert.IsTrue( brightest <= 1.0f / 12.0f,
			$"the ground is {brightest:0.###} of full and has to be under {1.0f / 12.0f:0.###}: " +
			"a window is 96% opaque, so anything lighter buries the four per cent of blur that is the glass" );
	}

	//The ground is one colour, so that what lies on it is told apart by being glass rather than by sitting on a lighter part of a gradient.
	[TestMethod]
	public void TheGroundIsFlat()
	{
		var tokens = Rinten.UI.EditorTokens.Text;

		Assert.IsFalse( string.IsNullOrEmpty( tokens ), "the tokens should be in the assembly" );

		var ground = tokens[tokens.IndexOf( "@mixin ground" )..];
		ground = ground[..ground.IndexOf( '}' )];

		Assert.IsFalse( ground.Contains( "gradient" ),
			"the ground is flat: a gradient under panels of one colour is what stops a window reading as one surface" );
	}

	//The colour a window is cleared to is the ground the panel over it paints, so the frames before it has laid out are not a different darkness.
	[TestMethod]
	public void TheClearColourIsTheGround()
	{
		Assert.AreEqual( Rinten.UI.EditorTokens.Colour( "ground" ), Editor.EditorGround.ClearColour );
	}
}
