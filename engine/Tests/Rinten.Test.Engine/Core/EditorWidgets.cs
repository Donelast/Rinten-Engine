using NativeEngine;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//The controls an editor window is built out of.
[TestClass]
[DoNotParallelize]
public class EditorWidgetTests
{
	//A root laid out to a rect, as a window lays its panels out.
	private static RootPanel Root( int width = 400, int height = 300 )
	{
		var root = new UiWindowRoot();

		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();

		return root;
	}

	private static void Layout( RootPanel root, int width = 400, int height = 300 )
	{
		root.TickInternal();
		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();
	}

	[TestMethod]
	public void AButtonSaysWhatItWasGivenAndAnswersWhenPressed()
	{
		var root = Root();

		var button = root.AddChild<EditorButton>();
		button.Text = "Compile";

		var pressed = 0;
		button.Pressed = () => pressed++;

		Layout( root );

		Assert.IsTrue( button.Box.Rect.Width > 0, "a button with a word in it has a size" );

		//Queued and answered on the next tick, as every panel event is.
		button.CreateEvent( new MousePanelEvent( "onclick", button, "mouseleft" ) );
		Layout( root );

		Assert.AreEqual( 1, pressed );

		//A button nobody may press does not answer, which is the whole of what being disabled means.
		button.Disabled = true;
		button.CreateEvent( new MousePanelEvent( "onclick", button, "mouseleft" ) );
		Layout( root );

		Assert.AreEqual( 1, pressed );
	}

	[TestMethod]
	public void TypingIntoAFieldPutsTheLettersInIt()
	{
		var root = Root();

		var entry = root.AddChild<EditorTextEntry>();

		Layout( root );

		foreach ( var letter in "hello" )
			entry.OnKeyTyped( letter );

		Assert.AreEqual( "hello", entry.Text );

		//Backspace is a key rather than a letter: it arrives as a button.
		entry.OnButtonTyped( new ButtonEvent( ButtonCode.KEY_BACKSPACE, true, KeyboardModifiers.None ) );

		Assert.AreEqual( "hell", entry.Text );
	}

	[TestMethod]
	public void AFieldHandsOverWhatWasTypedWhenReturnIsPressed()
	{
		var root = Root();

		var entry = root.AddChild<EditorTextEntry>();

		string submitted = null;
		entry.Submitted = x => submitted = x;

		Layout( root );

		foreach ( var letter in "quit" )
			entry.OnKeyTyped( letter );

		entry.OnButtonTyped( new ButtonEvent( ButtonCode.KEY_ENTER, true, KeyboardModifiers.None ) );

		Assert.AreEqual( "quit", submitted );
	}

	//The console shows earlier engine messages too; lines are added on tick, not mid-layout
	[TestMethod]
	public void TheConsoleShowsWhatTheEngineSaid()
	{
		var said = $"a line for the console to show {System.Guid.NewGuid()}";

		new Rinten.Diagnostics.Logger( "Test" ).Info( said );

		var root = Root( 600, 400 );

		var console = root.AddChild<EditorConsole>();

		//Twice: the first tick is where the console notices it has a parent and takes the log, the second is where what it took becomes panels.
		Layout( root, 600, 400 );
		Layout( root, 600, 400 );

		Assert.IsTrue( Lines( console ) > 0, "the console should be showing the log" );
		Assert.IsTrue( Shows( console, said ), "the line that was logged should be one of them" );
	}

	//Whether the console is showing a line with this in it.
	private static bool Shows( EditorConsole console, string text )
	{
		foreach ( var child in console.Children )
		{
			if ( !child.HasClass( "log" ) ) continue;

			foreach ( var line in child.Children )
			{
				foreach ( var part in line.Children )
				{
					if ( part is Label label && label.Text is not null && label.Text.Contains( text ) )
						return true;
				}
			}
		}

		return false;
	}

	//How many lines the console is showing.
	private static int Lines( EditorConsole console )
	{
		var count = 0;

		foreach ( var child in console.Children )
		{
			if ( !child.HasClass( "log" ) ) continue;

			foreach ( var line in child.Children )
				if ( line.HasClass( "line" ) ) count++;
		}

		return count;
	}

	//An unfocused key must not bounce between the window root and a child that bubbles it.
	[TestMethod]
	public void AnUnfocusedKeyDoesNotBounceBetweenTheRootAndItsChildren()
	{
		var root = Root();
		var child = root.AddChild<BubblingPanel>();

		Layout( root );

		root.OnButtonTyped( new ButtonEvent( ButtonCode.KEY_A, true, KeyboardModifiers.None ) );

		Assert.AreEqual( 1, child.Heard );
	}

	sealed class BubblingPanel : Panel
	{
		public int Heard;

		public override void OnButtonTyped( ButtonEvent e )
		{
			Heard++;
			base.OnButtonTyped( e );
		}
	}
}
