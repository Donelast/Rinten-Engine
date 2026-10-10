using NativeEngine;

namespace InputTests;

//What an input context holds across a flip, for the orders a key's events can come in between two flips.
[TestClass]
public class InputContextFlip
{
	const int Forward = 3;
	const ulong Bit = 1UL << Forward;

	static bool Held( Input.Context context ) => (context.ActionsCurrent & Bit) != 0;

	[TestMethod]
	public void HeldActionStaysDown()
	{
		var context = Input.Context.Create( "test" );

		context.PressAction( Forward );
		context.Flip();
		Assert.IsTrue( Held( context ) );

		context.Flip();
		Assert.IsTrue( Held( context ), "nothing came up, so it is still down" );
	}

	//A held key repeated by an input method arrives as a release and then a press - fcitx5 on Wayland
	//does this forty times a second. It is still held on the flip after, and the one after that.
	[TestMethod]
	public void ReleaseThenPressStaysDown()
	{
		var context = Input.Context.Create( "test" );

		context.PressAction( Forward );
		context.Flip();

		context.ReleaseAction( Forward );
		context.PressAction( Forward );
		context.Flip();
		Assert.IsTrue( Held( context ) );

		context.Flip();
		Assert.IsTrue( Held( context ), "the repeat must not drop a held action" );
	}

	//A tap inside one period is down for one flip and then up.
	[TestMethod]
	public void PressThenReleaseIsATap()
	{
		var context = Input.Context.Create( "test" );

		context.PressAction( Forward );
		context.ReleaseAction( Forward );
		context.Flip();
		Assert.IsTrue( Held( context ) );

		context.Flip();
		Assert.IsFalse( Held( context ) );
	}

	[TestMethod]
	public void RepeatedKeyStaysDown()
	{
		var context = Input.Context.Create( "test" );

		context.PressKey( ButtonCode.KEY_W );
		context.Flip();

		context.ReleaseKey( ButtonCode.KEY_W );
		context.PressKey( ButtonCode.KEY_W );
		context.Flip();
		context.Flip();

		Assert.IsTrue( context.KeysCurrent.Contains( ButtonCode.KEY_W ) );
	}
}
