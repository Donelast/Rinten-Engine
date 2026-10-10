using Android.Views;
using NativeEngine;
using Rinten;

namespace Rinten.Android;

//Keys - a keyboard plugged into the phone, the on-screen keyboard's own, the back button - handed to the engine.
internal static class KeyInput
{
	//Hands a key event on, and says whether it was the game's. The keys the system answers itself - the volume, power - are not.
	public static bool Forward( KeyEvent e, bool typing )
	{
		//A string the system typed in one go - a paste, a character with no key.
		if ( e.Action == KeyEventActions.Multiple && e.KeyCode == Keycode.Unknown )
		{
#pragma warning disable CA1422 //what a keyboard that still sends these sends
			if ( typing ) CoreInput.HostText( e.Characters );
#pragma warning restore CA1422
			return true;
		}

		var down = e.Action == KeyEventActions.Down;
		if ( !down && e.Action != KeyEventActions.Up ) return false;

		//Back is escape: it closes what is open - a menu, a panel, a text field - and the game decides what it means after that. It never ends the game.
		if ( e.KeyCode == Keycode.Back )
		{
			CoreInput.HostKey( ButtonCode.KEY_ESCAPE, down, e.RepeatCount > 0 );
			return true;
		}

		var code = ToButton( e.KeyCode );
		var handled = false;

		if ( code != ButtonCode.BUTTON_CODE_INVALID )
		{
			CoreInput.HostKey( code, down, e.RepeatCount > 0 );
			handled = true;
		}

		//What the key types, where something is being typed into - the way SDL says both a key and its text on a desktop.
		if ( typing && down && !e.IsCtrlPressed && !e.IsAltPressed )
		{
			var character = e.UnicodeChar;

			if ( character != 0 && !char.IsControl( (char)character ) )
			{
				CoreInput.HostText( char.ConvertFromUtf32( character ) );
				handled = true;
			}
		}

		return handled;
	}

	//The engine's name for an Android key, or invalid for one it has none for.
	public static ButtonCode ToButton( Keycode key )
	{
		if ( key >= Keycode.A && key <= Keycode.Z ) return ButtonCode.KEY_A + ( key - Keycode.A );
		if ( key >= Keycode.Num0 && key <= Keycode.Num9 ) return ButtonCode.KEY_0 + ( key - Keycode.Num0 );
		if ( key >= Keycode.Numpad0 && key <= Keycode.Numpad9 ) return ButtonCode.KEY_PAD_0 + ( key - Keycode.Numpad0 );
		if ( key >= Keycode.F1 && key <= Keycode.F12 ) return ButtonCode.KEY_F1 + ( key - Keycode.F1 );

		return key switch
		{
			Keycode.Space => ButtonCode.KEY_SPACE,
			Keycode.Enter => ButtonCode.KEY_ENTER,
			Keycode.NumpadEnter => ButtonCode.KEY_PAD_ENTER,
			Keycode.Del => ButtonCode.KEY_BACKSPACE,
			Keycode.ForwardDel => ButtonCode.KEY_DELETE,
			Keycode.Tab => ButtonCode.KEY_TAB,
			Keycode.Escape => ButtonCode.KEY_ESCAPE,
			Keycode.DpadUp => ButtonCode.KEY_UP,
			Keycode.DpadDown => ButtonCode.KEY_DOWN,
			Keycode.DpadLeft => ButtonCode.KEY_LEFT,
			Keycode.DpadRight => ButtonCode.KEY_RIGHT,
			Keycode.ShiftLeft => ButtonCode.KEY_LSHIFT,
			Keycode.ShiftRight => ButtonCode.KEY_RSHIFT,
			Keycode.CtrlLeft => ButtonCode.KEY_LCONTROL,
			Keycode.CtrlRight => ButtonCode.KEY_RCONTROL,
			Keycode.AltLeft => ButtonCode.KEY_LALT,
			Keycode.AltRight => ButtonCode.KEY_RALT,
			Keycode.MoveHome => ButtonCode.KEY_HOME,
			Keycode.MoveEnd => ButtonCode.KEY_END,
			Keycode.PageUp => ButtonCode.KEY_PAGEUP,
			Keycode.PageDown => ButtonCode.KEY_PAGEDOWN,
			Keycode.Insert => ButtonCode.KEY_INSERT,
			Keycode.CapsLock => ButtonCode.KEY_CAPSLOCK,
			Keycode.Minus => ButtonCode.KEY_MINUS,
			Keycode.Equals => ButtonCode.KEY_EQUAL,
			Keycode.LeftBracket => ButtonCode.KEY_LBRACKET,
			Keycode.RightBracket => ButtonCode.KEY_RBRACKET,
			Keycode.Semicolon => ButtonCode.KEY_SEMICOLON,
			Keycode.Apostrophe => ButtonCode.KEY_APOSTROPHE,
			Keycode.Grave => ButtonCode.KEY_BACKQUOTE,
			Keycode.Comma => ButtonCode.KEY_COMMA,
			Keycode.Period => ButtonCode.KEY_PERIOD,
			Keycode.Slash => ButtonCode.KEY_SLASH,
			Keycode.Backslash => ButtonCode.KEY_BACKSLASH,
			Keycode.NumpadDivide => ButtonCode.KEY_PAD_DIVIDE,
			Keycode.NumpadMultiply => ButtonCode.KEY_PAD_MULTIPLY,
			Keycode.NumpadSubtract => ButtonCode.KEY_PAD_MINUS,
			Keycode.NumpadAdd => ButtonCode.KEY_PAD_PLUS,
			Keycode.NumpadDot => ButtonCode.KEY_PAD_DECIMAL,
			_ => ButtonCode.BUTTON_CODE_INVALID,
		};
	}
}
