using System;
namespace LayoutTests.Core;

//The seam casts style enums to layout enums by value; only Justify is offset by one (leading Auto)
[TestClass]
public class EnumParityTests
{
	private static void AssertParity<TPublic, TLayout>( int offset = 0 ) where TPublic : struct, Enum where TLayout : struct, Enum
	{
		foreach ( var value in Enum.GetValues<TPublic>() )
		{
			var name = value.ToString();
			var numeric = Convert.ToInt32( value );

			Assert.IsTrue( Enum.TryParse<TLayout>( name, out var layoutValue ), $"{typeof( TLayout ).Name} is missing {typeof( TPublic ).Name}.{name}" );
			Assert.AreEqual( numeric + offset, Convert.ToInt32( layoutValue ), $"{typeof( TPublic ).Name}.{name} = {numeric} but {typeof( TLayout ).Name}.{name} = {Convert.ToInt32( layoutValue )}" );
		}
	}

	[TestMethod] public void Display() => AssertParity<Rinten.UI.DisplayMode, Rinten.Layout.Display>();
	[TestMethod] public void Position() => AssertParity<Rinten.UI.PositionMode, Rinten.Layout.PositionType>();
	[TestMethod] public void FlexDirection() => AssertParity<Rinten.UI.FlexDirection, Rinten.Layout.FlexDirection>();
	[TestMethod] public void Justify() => AssertParity<Rinten.UI.Justify, Rinten.Layout.Justify>();
	[TestMethod] public void Align() => AssertParity<Rinten.UI.Align, Rinten.Layout.Align>();
	[TestMethod] public void Wrap() => AssertParity<Rinten.UI.Wrap, Rinten.Layout.Wrap>();
}
