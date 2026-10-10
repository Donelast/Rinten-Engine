namespace Editor;

//The keyboard, as the editor's widgets name it.
internal static class EditorKeys
{
	/// <summary>The key a name means, in either spelling.</summary>
	public static KeyCode FromName( string name )
	{
		if ( string.IsNullOrEmpty( name ) ) return 0;

		name = name.Trim().ToLowerInvariant();

		switch ( name )
		{
			case "escape": case "esc": return KeyCode.Escape;
			case "tab": return KeyCode.Tab;
			case "backspace": return KeyCode.Backspace;
			case "enter": case "return": return KeyCode.Return;
			case "keypadenter": case "kp_enter": return KeyCode.Enter;
			case "delete": case "del": return KeyCode.Delete;
			case "insert": case "ins": return KeyCode.Insert;
			case "home": return KeyCode.Home;
			case "end": return KeyCode.End;
			case "left": return KeyCode.Left;
			case "up": return KeyCode.Up;
			case "right": return KeyCode.Right;
			case "down": return KeyCode.Down;
			case "pageup": case "pgup": return KeyCode.PageUp;
			case "pagedown": case "pgdn": return KeyCode.PageDown;
			case "space": return KeyCode.Space;
			case "shift": return KeyCode.Shift;
			case "ctrl": case "control": return KeyCode.Control;
			case "alt": return KeyCode.Alt;
			case "capslock": return KeyCode.CapsLock;
			case "numlock": return KeyCode.NumLock;
			case "scrolllock": return KeyCode.ScrollLock;
			case "pause": return KeyCode.Pause;
			case "printscreen": return KeyCode.Print;

			//The keypad, as the key printed on it. A widget being typed into wants the character; which half
			//of the board it came from is the shortcut system's business and it has its own names for them.
			case "kp_add": case "kp_plus": return KeyCode.Plus;
			case "kp_minus": return KeyCode.Minus;
			case "kp_multiply": return KeyCode.Asterisk;
			case "kp_divide": return KeyCode.Slash;
			case "kp_decimal": return KeyCode.Period;
		}

		//The function keys, however far up the board goes.
		if ( name.Length > 1 && name[0] == 'f' && int.TryParse( name[1..], out var number )
			&& number is >= 1 and <= 35 )
		{
			return KeyCode.F1 + (number - 1);
		}

		//And the keypad digits, as digits.
		if ( name.Length == 4 && name.StartsWith( "kp_", System.StringComparison.Ordinal )
			&& name[3] is >= '0' and <= '9' )
		{
			return KeyCode.Num0 + (name[3] - '0');
		}

		if ( name.Length == 1 )
		{
			var letter = char.ToUpperInvariant( name[0] );

			if ( letter is >= 'A' and <= 'Z' ) return KeyCode.A + (letter - 'A');
			if ( letter is >= '0' and <= '9' ) return KeyCode.Num0 + (letter - '0');

			//What is written on the rest of the keys is what they are called.
			switch ( name[0] )
			{
				case '[': return KeyCode.BracketLeft;
				case ']': return KeyCode.BracketRight;
				case '\\': return KeyCode.Backslash;
				case '/': return KeyCode.Slash;
				case ';': return KeyCode.Semicolon;
				case '\'': return KeyCode.Apostrophe;
				case ',': return KeyCode.Comma;
				case '.': return KeyCode.Period;
				case '-': return KeyCode.Minus;
				case '=': return KeyCode.Equal;
				case '`': return KeyCode.QuoteLeft;
			}
		}

		return 0;
	}

	public static string ToName( KeyCode code )
	{
		switch ( code )
		{
			case KeyCode.Escape: return "escape";
			case KeyCode.Tab: return "tab";
			case KeyCode.Backspace: return "backspace";
			case KeyCode.Return: return "enter";
			case KeyCode.Enter: return "keypadenter";
			case KeyCode.Delete: return "delete";
			case KeyCode.Insert: return "insert";
			case KeyCode.Home: return "home";
			case KeyCode.End: return "end";
			case KeyCode.Left: return "left";
			case KeyCode.Up: return "up";
			case KeyCode.Right: return "right";
			case KeyCode.Down: return "down";
			case KeyCode.PageUp: return "pageup";
			case KeyCode.PageDown: return "pagedown";
			case KeyCode.Space: return "space";
			case KeyCode.Shift: return "shift";
			case KeyCode.Control: return "ctrl";
			case KeyCode.Alt: return "alt";
		}

		switch ( code )
		{
			case KeyCode.BracketLeft: return "[";
			case KeyCode.BracketRight: return "]";
			case KeyCode.Backslash: return "\\";
			case KeyCode.Slash: return "/";
			case KeyCode.Semicolon: return ";";
			case KeyCode.Apostrophe: return "'";
			case KeyCode.Comma: return ",";
			case KeyCode.Period: return ".";
			case KeyCode.Minus: return "-";
			case KeyCode.Equal: return "=";
			case KeyCode.QuoteLeft: return "`";
		}

		if ( code >= KeyCode.A && code <= KeyCode.Z ) return ((char)('a' + (code - KeyCode.A))).ToString();
		if ( code >= KeyCode.Num0 && code <= KeyCode.Num9 ) return ((char)('0' + (code - KeyCode.Num0))).ToString();
		if ( code >= KeyCode.F1 && code <= KeyCode.F12 ) return $"f{1 + (code - KeyCode.F1)}";

		return "";
	}

	/// <summary>SDL scancode for a key, to query held state; only keys the editor asks about, others are 0</summary>
	public static int ToScancode( KeyCode code )
	{
		switch ( code )
		{
			case KeyCode.Return: return 40;
			case KeyCode.Escape: return 41;
			case KeyCode.Backspace: return 42;
			case KeyCode.Tab: return 43;
			case KeyCode.Space: return 44;
			case KeyCode.Insert: return 73;
			case KeyCode.Home: return 74;
			case KeyCode.PageUp: return 75;
			case KeyCode.Delete: return 76;
			case KeyCode.End: return 77;
			case KeyCode.PageDown: return 78;
			case KeyCode.Right: return 79;
			case KeyCode.Left: return 80;
			case KeyCode.Down: return 81;
			case KeyCode.Up: return 82;
			case KeyCode.Enter: return 88;
			case KeyCode.Control: return 224;
			case KeyCode.Shift: return 225;
			case KeyCode.Alt: return 226;

			//The keys with something printed on them that is not a letter, where they sit on an American board.
			case KeyCode.Minus: return 45;
			case KeyCode.Equal: return 46;
			case KeyCode.BracketLeft: return 47;
			case KeyCode.BracketRight: return 48;
			case KeyCode.Backslash: return 49;
			case KeyCode.Semicolon: return 51;
			case KeyCode.Apostrophe: return 52;
			case KeyCode.QuoteLeft: return 53;
			case KeyCode.Comma: return 54;
			case KeyCode.Period: return 55;
			case KeyCode.Slash: return 56;
			case KeyCode.CapsLock: return 57;

			//Two of these are only on the keypad - there is no plus and no asterisk of their own on the main row, so a bind on one of them can only mean that key.
			case KeyCode.Asterisk: return 85;
			case KeyCode.Plus: return 87;
		}

		//Letters run from A, and the digits run from one with zero at the end, which is the order they are on the board.
		if ( code >= KeyCode.A && code <= KeyCode.Z ) return 4 + (code - KeyCode.A);
		if ( code == KeyCode.Num0 ) return 39;
		if ( code >= KeyCode.Num1 && code <= KeyCode.Num9 ) return 30 + (code - KeyCode.Num1);
		if ( code >= KeyCode.F1 && code <= KeyCode.F12 ) return 58 + (code - KeyCode.F1);
		if ( code >= KeyCode.F13 && code <= KeyCode.F24 ) return 104 + (code - KeyCode.F13);

		return 0;
	}
}
