using NativeEngine;
using Rinten.UI;
using System;

namespace Editor.Mcp;

/// <summary>A pointer and a keyboard for an editor window that nobody is sitting at. Every tool here puts events in front of the window exactly where the window system would - see CoreWindow.PretendPointer and its siblings - and then lets the window draw a frame or two, so the click travels the road a real one does: hover, press, release, the widget's own handler. Each answers with what the pointer was over before and after, and what has the focus, so a click that landed on the wrong thing says so.</summary>
[McpToolset( "ui", "Drive the editor's own UI - move the pointer, click, drag, scroll, press keys and type, at coordinates or on a panel by id" )]
internal static class UiInputTools
{
	//How long to give a window to draw a frame before deciding it isn't drawing. The
	//editor idles at a few frames a second when nothing moves, so this is generous.
	static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds( 3 );

	[McpTool( "ui_move" )]
	[Description( "Move the pointer to a point in an editor window - to a panel by id, or to window pixel coordinates - and say what's under it afterwards. Hover is what makes tooltips, hover styles and drop targets happen." )]
	public static async Task<UiInputResult> UiMove(
		[Description( "A panel id from panel_tree or panel_find; the pointer goes to its centre." )] string panel = "",
		[Description( "Window pixel coordinates as 'x,y', instead of a panel." )] string at = "",
		[Description( "Which window, as window_list names it. Empty is the main window, or the panel's." )] string window = "" )
	{
		var (shell, point) = Target( panel, at, window );
		var before = Under( shell );

		shell.PretendPointer( point, MouseButtons.None, MouseButtons.None );
		await Frames( shell, 2 );

		return Result( shell, point, before );
	}

	[McpTool( "ui_click" )]
	[Description( "Click in an editor window - on a panel by id, or at window pixel coordinates - with a mouse button, once or more, with modifier keys held. Does what a person's click does: hovers first, presses, releases, and lets the window run the widget's handler. Answers with what was under the pointer before and after, and what has the focus now - a click on a field focuses it, a click on a button runs it. Menus, dialogs and confirmations open for real; find them with window_list and panel_find afterwards." )]
	public static async Task<UiInputResult> UiClick(
		[Description( "A panel id from panel_tree or panel_find; the click lands on its centre." )] string panel = "",
		[Description( "Window pixel coordinates as 'x,y', instead of a panel." )] string at = "",
		[Description( "left, right or middle. Default left." )] string button = "left",
		[Description( "How many clicks - 2 for a double click. Default 1, max 3." ), Range( 1, 3 )] int count = 1,
		[Description( "Keys held during the click, like 'ctrl', 'shift' or 'ctrl+shift'." )] string modifiers = "",
		[Description( "Which window, as window_list names it. Empty is the main window, or the panel's." )] string window = "" )
	{
		var (shell, point) = Target( panel, at, window );
		var pressed = Button( button );
		var held = Modifiers( modifiers );
		var before = Under( shell );

		shell.PretendPointer( point, MouseButtons.None, MouseButtons.None );
		await Frames( shell, 1 );

		using ( Holding( shell, held ) )
		{
			for ( var i = 0; i < count; i++ )
			{
				shell.PretendPointer( point, pressed, MouseButtons.None );
				CoreCursor.PretendedButtons |= pressed;
				await Frames( shell, 1 );

				shell.PretendPointer( point, MouseButtons.None, pressed );
				CoreCursor.PretendedButtons &= ~pressed;
				await Frames( shell, 1 );
			}
		}

		//A panel queues what a click asks for and runs it on the tick after, and a widget's handler may open a menu or a dialog that wants a frame of its own.
		await Frames( shell, 2 );

		return Result( shell, point, before );
	}

	[McpTool( "ui_drag" )]
	[Description( "Drag in an editor window: press at one place, move the pointer to another over several frames, release. For sliders, splitters, reordering, moving a floating window, drawing a marquee. Points are panel ids or 'x,y' window pixels; the drag can start on one panel and end on another." )]
	public static async Task<UiInputResult> UiDrag(
		[Description( "Where the drag starts: a panel id, or 'x,y'." )] string from,
		[Description( "Where it ends: a panel id, or 'x,y'." )] string to,
		[Description( "left, right or middle. Default left." )] string button = "left",
		[Description( "How many pointer moves between the two points. Default 8, max 60 - more for a widget that reads speed." ), Range( 1, 60 )] int steps = 8,
		[Description( "Keys held during the drag, like 'shift'." )] string modifiers = "",
		[Description( "Which window, as window_list names it. Empty is the main window, or the panel's." )] string window = "" )
	{
		var (shell, start) = Point( from, window );
		var (other, end) = Point( to, window );

		if ( !ReferenceEquals( shell, other ) )
			throw new Exception( $"The drag would start in '{shell.Title}' and end in '{other.Title}' - a drag stays in one window." );

		var pressed = Button( button );
		var held = Modifiers( modifiers );
		var before = Under( shell );

		shell.PretendPointer( start, MouseButtons.None, MouseButtons.None );
		await Frames( shell, 1 );

		using ( Holding( shell, held ) )
		{
			shell.PretendPointer( start, pressed, MouseButtons.None );
			CoreCursor.PretendedButtons |= pressed;
			await Frames( shell, 1 );

			for ( var i = 1; i <= steps; i++ )
			{
				shell.PretendPointer( Vector2.Lerp( start, end, (float)i / steps ), MouseButtons.None, MouseButtons.None );
				await Frames( shell, 1 );
			}

			shell.PretendPointer( end, MouseButtons.None, pressed );
			CoreCursor.PretendedButtons &= ~pressed;
			await Frames( shell, 2 );
		}

		return Result( shell, end, before );
	}

	[McpTool( "ui_wheel" )]
	[Description( "Turn the mouse wheel over a point in an editor window - a panel by id or 'x,y' - to scroll a list, a tree or a text, or zoom a view. Positive delta scrolls up (the way a wheel rolled away from you does), negative down; one notch is 1." )]
	public static async Task<UiInputResult> UiWheel(
		[Description( "Notches to turn: positive up, negative down. Default -3." )] float delta = -3,
		[Description( "A panel id to scroll over." )] string panel = "",
		[Description( "Window pixel coordinates as 'x,y', instead of a panel." )] string at = "",
		[Description( "Sideways notches, for a horizontal scroll. Default 0." )] float horizontal = 0,
		[Description( "Which window, as window_list names it." )] string window = "" )
	{
		var (shell, point) = Target( panel, at, window );
		var before = Under( shell );

		shell.PretendPointer( point, MouseButtons.None, MouseButtons.None );
		await Frames( shell, 1 );

		shell.PretendWheel( new Vector2( horizontal, delta ) );
		await Frames( shell, 2 );

		return Result( shell, point, before );
	}

	[McpTool( "ui_key" )]
	[Description( "Press and release a key in an editor window, with modifiers: 'enter', 'escape', 'delete', 'f5', 'a', 'tab', 'space', 'up', 'ctrl+s', 'ctrl+shift+z'. Goes to the window's shortcuts first and then to the focused panel, as a real key does. For typing text into a field use ui_type - a key is a key, not a letter." )]
	public static async Task<UiInputResult> UiKey(
		[Description( "The key with its modifiers, like 'enter', 'ctrl+z' or 'ctrl+shift+s'. Key names are the engine's: letters, digits, f1-f12, enter, escape, tab, space, backspace, delete, insert, home, end, pageup, pagedown, up, down, left, right." )] string key,
		[Description( "Which window gets the key, as window_list names it. Empty is the one with the keyboard, or the main window." )] string window = "" )
	{
		var shell = string.IsNullOrWhiteSpace( window ) ? CoreUiWindow.Focused ?? WindowTools.Resolve( "" ) : WindowTools.Resolve( window );
		var (code, held) = Key( key );
		var before = Under( shell );

		using ( Holding( shell, held ) )
		{
			shell.PretendKey( code, held, down: true );
			await Frames( shell, 1 );
			shell.PretendKey( code, held, down: false );
			await Frames( shell, 2 );
		}

		return Result( shell, null, before );
	}

	[McpTool( "ui_type" )]
	[Description( "Type text into whatever has the focus in an editor window - click a field with ui_click first. Letters arrive the way composed text does, so the field takes them as typed; a newline in the text is an enter key, a tab a tab key. Doesn't select or clear what's there: 'ctrl+a' with ui_key first replaces it." )]
	public static async Task<UiInputResult> UiType(
		[Description( "The text to type." )] string text,
		[Description( "Which window, as window_list names it. Empty is the one with the keyboard, or the main window." )] string window = "" )
	{
		if ( string.IsNullOrEmpty( text ) )
			throw new Exception( "Give some text to type." );

		var shell = string.IsNullOrWhiteSpace( window ) ? CoreUiWindow.Focused ?? WindowTools.Resolve( "" ) : WindowTools.Resolve( window );
		var before = Under( shell );

		if ( InputFocus.Current is not { IsValid: true } )
			throw new Exception( "Nothing has the focus in the editor - ui_click a field first, then type." );

		//A line at a time: letters as text, the breaks between them as the key a person would press there.
		var lines = text.Replace( "\r\n", "\n" ).Split( '\n' );

		for ( var i = 0; i < lines.Length; i++ )
		{
			if ( i > 0 )
			{
				shell.PretendKey( ButtonCode.KEY_ENTER, KeyboardModifiers.None, down: true );
				await Frames( shell, 1 );
				shell.PretendKey( ButtonCode.KEY_ENTER, KeyboardModifiers.None, down: false );
				await Frames( shell, 1 );
			}

			foreach ( var piece in lines[i].Split( '\t' ).Select( ( part, index ) => (part, index) ) )
			{
				if ( piece.index > 0 )
				{
					shell.PretendKey( ButtonCode.KEY_TAB, KeyboardModifiers.None, down: true );
					await Frames( shell, 1 );
					shell.PretendKey( ButtonCode.KEY_TAB, KeyboardModifiers.None, down: false );
					await Frames( shell, 1 );
				}

				if ( piece.part.Length == 0 ) continue;

				shell.PretendTyped( piece.part );
				await Frames( shell, 1 );
			}
		}

		await Frames( shell, 1 );

		return Result( shell, null, before );
	}

	//── Where and what ────────────────────────────────────────────────────────

	//The window and point a panel id or 'x,y' means.
	static (CoreUiWindow Shell, Vector2 Point) Target( string panel, string at, string window )
	{
		if ( !string.IsNullOrWhiteSpace( panel ) && !string.IsNullOrWhiteSpace( at ) )
			throw new Exception( "Give a panel or a point, not both." );

		if ( string.IsNullOrWhiteSpace( panel ) && string.IsNullOrWhiteSpace( at ) )
			throw new Exception( "Say where: a panel id from panel_find, or window pixel coordinates as 'x,y'." );

		return Point( string.IsNullOrWhiteSpace( panel ) ? at : panel, window );
	}

	//A place that is either a panel id or 'x,y'.
	static (CoreUiWindow Shell, Vector2 Point) Point( string place, string window )
	{
		place = place?.Trim() ?? "";

		if ( place.StartsWith( 'p' ) && !place.Contains( ',' ) )
		{
			var target = PanelHandles.Resolve( place );
			var shell = CoreUiWindow.Holding( target )
				?? throw new Exception( $"Panel {place} is the game's, not an editor window's - these tools drive the editor. Game input is game_key and game_look." );

			if ( !target.IsVisible )
				throw new Exception( $"Panel {place} isn't visible, so nothing can be clicked on it. panel_inspect says why." );

			if ( PanelTools.IsOffscreen( target ) )
				throw new Exception( $"Panel {place} is scrolled or clipped out of view at {PanelTools.RectText( target.Box.Rect )} - a click there reaches nothing. ui_wheel over its list to bring it in, or resize the window." );

			var rect = target.Box.Rect;

			return (shell, new Vector2( rect.Left + rect.Width / 2, rect.Top + rect.Height / 2 ));
		}

		var parts = place.Split( ',', StringSplitOptions.TrimEntries );

		if ( parts.Length != 2 || !float.TryParse( parts[0], System.Globalization.CultureInfo.InvariantCulture, out var x ) || !float.TryParse( parts[1], System.Globalization.CultureInfo.InvariantCulture, out var y ) )
			throw new Exception( $"'{place}' is neither a panel id like 'p12' nor a point like '640,360'." );

		var resolved = WindowTools.Resolve( window );
		var (width, height) = resolved.Size;

		if ( x < 0 || y < 0 || x >= width || y >= height )
			throw new Exception( $"{x:0},{y:0} is outside '{resolved.Title}', which is {width}x{height}." );

		return (resolved, new Vector2( x, y ));
	}

	static MouseButtons Button( string button ) => button?.Trim().ToLowerInvariant() switch
	{
		"" or "left" or "l" => MouseButtons.Left,
		"right" or "r" => MouseButtons.Right,
		"middle" or "m" => MouseButtons.Middle,
		"back" => MouseButtons.Back,
		"forward" => MouseButtons.Forward,
		_ => throw new Exception( $"'{button}' isn't a mouse button - left, right or middle." )
	};

	static KeyboardModifiers Modifiers( string modifiers )
	{
		var held = KeyboardModifiers.None;

		foreach ( var part in (modifiers ?? "").Split( ['+', ' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			held |= part.ToLowerInvariant() switch
			{
				"ctrl" or "control" => KeyboardModifiers.Ctrl,
				"shift" => KeyboardModifiers.Shift,
				"alt" => KeyboardModifiers.Alt,
				_ => throw new Exception( $"'{part}' isn't a modifier - ctrl, shift or alt." )
			};
		}

		return held;
	}

	//'ctrl+shift+s' as the key and the modifiers held with it.
	static (ButtonCode Code, KeyboardModifiers Held) Key( string key )
	{
		var parts = (key ?? "").Split( '+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

		if ( parts.Length == 0 )
			throw new Exception( "Give a key, like 'enter' or 'ctrl+s'." );

		var held = parts.Length > 1 ? Modifiers( string.Join( '+', parts[..^1] ) ) : KeyboardModifiers.None;
		var name = parts[^1];

		var code = Rinten.EngineInput.StringToButtonCode( name );

		if ( code == ButtonCode.BUTTON_CODE_INVALID )
			code = Rinten.EngineInput.StringToButtonCode( name.ToUpperInvariant() );

		if ( code == ButtonCode.BUTTON_CODE_INVALID )
			throw new Exception( $"'{name}' isn't a key the engine knows. Letters and digits are themselves; the rest are enter, escape, tab, space, backspace, delete, insert, home, end, pageup, pagedown, up, down, left, right, f1-f12." );

		return (code, held);
	}

	//Modifier keys held for the length of a using: down before, up after, both in the window's own events and in what the widgets that ask the desktop see.
	static IDisposable Holding( CoreUiWindow shell, KeyboardModifiers held )
	{
		if ( held == KeyboardModifiers.None ) return null;

		foreach ( var (flag, code) in ModifierKeys )
		{
			if ( held.HasFlag( flag ) ) shell.PretendKey( code, held, down: true );
		}

		CoreCursor.PretendedModifiers |= held;

		return new Release( shell, held );
	}

	static readonly (KeyboardModifiers Flag, ButtonCode Code)[] ModifierKeys =
	[
		(KeyboardModifiers.Ctrl, ButtonCode.KEY_LCONTROL),
		(KeyboardModifiers.Shift, ButtonCode.KEY_LSHIFT),
		(KeyboardModifiers.Alt, ButtonCode.KEY_LALT),
	];

	sealed class Release( CoreUiWindow shell, KeyboardModifiers held ) : IDisposable
	{
		public void Dispose()
		{
			CoreCursor.PretendedModifiers &= ~held;

			foreach ( var (flag, code) in ModifierKeys )
			{
				if ( held.HasFlag( flag ) ) shell.PretendKey( code, KeyboardModifiers.None, down: false );
			}
		}
	}

	//Lets the window draw some frames, so what was put in front of it is read, laid
	//out and drawn. Off the main thread between polls; every window draws on it.
	internal static Task Frames( CoreUiWindow shell, int count )
	{
		var target = shell.FramesDrawn + count;

		return Frame.Until( () => shell.FramesDrawn >= target, FrameTimeout,
			() => $"Window '{shell.Title}' hasn't drawn a frame in {FrameTimeout.TotalSeconds:0} seconds - minimized, hidden, or the editor is stuck. window_list says." );
	}

	static string Under( CoreUiWindow shell ) => Describe( shell.Hovered );

	static string Describe( Panel panel )
	{
		if ( panel is not { IsValid: true } ) return "nothing";

		return PanelTools.Line( panel );
	}

	static UiInputResult Result( CoreUiWindow shell, Vector2? point, string before )
	{
		var focused = InputFocus.Current;

		return new UiInputResult
		{
			Window = shell.Title,
			At = point is { } p ? $"{p.x:0},{p.y:0}" : null,
			Before = before,
			After = Under( shell ),
			Focus = focused is { IsValid: true } ? PanelTools.Line( focused ) : "nothing",
			Windows = CoreUiWindow.AllOpen.Count,
		};
	}
}

//What a ui_ tool answers: what the pointer was over before and after, and what has the focus.
internal class UiInputResult
{
	public string Window { get; set; }

	[Description( "Where the pointer ended up, in window pixels." )]
	public string At { get; set; }

	[Description( "The panel under the pointer before, as panel_tree prints it." )]
	public string Before { get; set; }

	[Description( "The panel under the pointer after - what the click reached." )]
	public string After { get; set; }

	[Description( "The panel with the keyboard focus after." )]
	public string Focus { get; set; }

	[Description( "How many windows are open now - one more than before means a menu, popup or dialog opened; window_list names it." )]
	public int Windows { get; set; }
}
