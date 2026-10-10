using Rinten.UI;

namespace UITests;

//'none' and colour-only shorthands reset their longhands, overriding base rules as on the web
[TestClass]
public class ShorthandResetTest
{
	//Cascades a base rule then a more specific override rule, the way the style system does.
	static Styles Cascade( Styles baseRule, Styles overrideRule )
	{
		var result = new Styles();
		result.Add( baseRule );
		result.Add( overrideRule );
		return result;
	}

	static Styles Rule( params (string prop, string value)[] declarations )
	{
		var s = new Styles();
		foreach ( var (prop, value) in declarations )
			s.Set( prop, value );
		return s;
	}

	//background: <colour> clears a background-image set by a base class.
	[TestMethod]
	public void BackgroundColorClearsBaseImage()
	{
		var result = Cascade(
			Rule( ("background-image", "url(x.png)") ),
			Rule( ("background", "#fff") ) );

		Assert.AreSame( Styles.NoImage, result._backgroundImage );
		Assert.AreEqual( Color.White, result.BackgroundColor.Value );
	}

	//background: none clears a base class's background image and colour.
	[TestMethod]
	public void BackgroundNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("background-image", "url(x.png)"), ("background-color", "red") ),
			Rule( ("background", "none") ) );

		Assert.AreSame( Styles.NoImage, result._backgroundImage );
		Assert.AreEqual( Color.Transparent, result.BackgroundColor.Value );
	}

	//border: none clears a base class's border.
	[TestMethod]
	public void BorderNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("border", "4px solid red") ),
			Rule( ("border", "none") ) );

		Assert.AreEqual( 0, result.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 0, result.BorderTopWidth.Value.Value );
		Assert.AreEqual( 0, result.BorderRightWidth.Value.Value );
		Assert.AreEqual( 0, result.BorderBottomWidth.Value.Value );
	}

	//filter: none clears a base class's filter.
	[TestMethod]
	public void FilterNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("filter", "blur(8px)") ),
			Rule( ("filter", "none") ) );

		Assert.AreEqual( 0, result.FilterBlur.Value.Value );
	}

	//backdrop-filter: none clears a base class's backdrop-filter.
	[TestMethod]
	public void BackdropFilterNoneClearsBase()
	{
		var result = Cascade(
			Rule( ("backdrop-filter", "blur(8px)") ),
			Rule( ("backdrop-filter", "none") ) );

		Assert.AreEqual( 0, result.BackdropFilterBlur.Value.Value );
	}
}
