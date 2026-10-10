using Editor;
using Rinten;
using System.Collections.Generic;
using NativeEngine;
using Rinten.UI;

namespace EngineTests;

//The editor's own text field, used where no addon TextEntry is mounted (launcher, editor shell)
[TestClass]
[DoNotParallelize]
public class TextFieldControlTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A field in a window, laid out - which is when its text exists as text rather than as a string waiting to be measured.
	private static TextField Field()
	{
		var root = new UiWindowRoot();
		var field = new TextField { Parent = root };

		root.TickInternal();
		root.PreLayout( new Rect( 0, 0, 400, 300 ) );
		root.CalculateLayout();
		root.PostLayout();

		return field;
	}

	//A key pressed, as the window would send it.
	private static void Press( TextField field, ButtonCode button, bool ctrl = false, bool shift = false )
	{
		var modifiers = KeyboardModifiers.None;

		if ( ctrl ) modifiers |= KeyboardModifiers.Ctrl;
		if ( shift ) modifiers |= KeyboardModifiers.Shift;

		field.OnButtonTyped( new ButtonEvent( button, true, modifiers ) );
	}

	private static void Type( TextField field, string text )
	{
		foreach ( var letter in text ) field.OnKeyTyped( letter );
	}

	[TestMethod]
	public void WhatIsTypedIsWhatItSays()
	{
		var field = Field();

		Type( field, "scene" );

		Assert.AreEqual( "scene", field.Value );
	}

	[TestMethod]
	public void BackspaceTakesTheLetterBeforeTheCaret()
	{
		var field = Field();

		Type( field, "scene" );
		Press( field, ButtonCode.KEY_BACKSPACE );

		Assert.AreEqual( "scen", field.Value );
	}

	[TestMethod]
	public void TheCaretMovesAndTypingGoesWhereItIs()
	{
		var field = Field();

		Type( field, "scne" );

		//Back over the last two, then the missing letter.
		Press( field, ButtonCode.KEY_LEFT );
		Press( field, ButtonCode.KEY_LEFT );

		Type( field, "e" );

		Assert.AreEqual( "scene", field.Value );
	}

	[TestMethod]
	public void SelectingEverythingAndTypingReplacesIt()
	{
		var field = Field();

		Type( field, "scene" );

		Press( field, ButtonCode.KEY_A, ctrl: true );

		Type( field, "map" );

		Assert.AreEqual( "map", field.Value );
	}

	[TestMethod]
	public void EscapeEmptiesAFieldThatHasSomethingInIt()
	{
		var field = Field();

		Type( field, "scene" );

		Press( field, ButtonCode.KEY_ESCAPE );

		Assert.AreEqual( string.Empty, field.Value );
	}

	[TestMethod]
	public void EveryChangeIsToldAsItIsMade()
	{
		var field = Field();
		var heard = new List<string>();

		field.ValueChanged = x => heard.Add( x );

		Type( field, "map" );

		CollectionAssert.AreEqual( new[] { "m", "ma", "map" }, heard );
	}

	[TestMethod]
	public void ReturnSaysSoRatherThanTypingALine()
	{
		var field = Field();
		string submitted = null;

		field.Submitted = x => submitted = x;

		Type( field, "map" );
		Press( field, ButtonCode.KEY_ENTER );

		Assert.AreEqual( "map", submitted );
		Assert.AreEqual( "map", field.Value );
	}

	//A field that has the focus keeps what it was given: the editor's shortcuts are not fired by the letters somebody is writing.
	[TestMethod]
	public void KeysDoNotCarryOnPastAFieldThatIsBeingTypedIn()
	{
		var field = Field();
		var e = new ButtonEvent( ButtonCode.KEY_A, true, KeyboardModifiers.None );

		field.OnButtonTyped( e );

		Assert.IsTrue( e.StopPropagation );
	}
}
