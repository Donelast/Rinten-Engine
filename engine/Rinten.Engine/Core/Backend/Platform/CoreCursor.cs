using System;
using System.Runtime.InteropServices;

namespace Rinten;

//Where the pointer is and what is held down, asked of the window system rather than of an event.
internal static partial class CoreCursor
{
	private const string SDL = "SDL3";

	[LibraryImport( SDL, EntryPoint = "SDL_GetGlobalMouseState" )]
	private static partial uint SDL_GetGlobalMouseState( out float x, out float y );

	[LibraryImport( SDL, EntryPoint = "SDL_WarpMouseGlobal" )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_WarpMouseGlobal( float x, float y );

	[LibraryImport( SDL, EntryPoint = "SDL_GetModState" )]
	private static partial ushort SDL_GetModState();

	[LibraryImport( SDL, EntryPoint = "SDL_GetKeyboardState" )]
	private static partial IntPtr SDL_GetKeyboardState( out int count );

	[LibraryImport( SDL, EntryPoint = "SDL_GetWindowDisplayScale" )]
	private static partial float SDL_GetWindowDisplayScale( IntPtr window );

	//Whether there is a desktop pointer to ask about: the core loaded, and an SDL beside it - see
	//CoreWindow.HasSdl. A phone has neither a pointer nor the library, and answers everything here with nothing.
	private static bool Here => CoreNative.Available && CoreWindow.HasSdl;

	/// <summary>The pointer, in desktop pixels.</summary>
	public static Vector2 Position
	{
		get
		{
			if ( !Here ) return default;

			SDL_GetGlobalMouseState( out var x, out var y );

			return new Vector2( x, y );
		}

		set
		{
			if ( !Here ) return;

			SDL_WarpMouseGlobal( value.x, value.y );
		}
	}

	//SDL's own numbers, which are what the two below are read out of.

	private const ushort ModShift = 0x0003;
	private const ushort ModCtrl = 0x00C0;
	private const ushort ModAlt = 0x0300;
	private const ushort ModGui = 0x0C00;

	//SDL global mouse state bits: middle is bit 1 and right bit 2, unlike MouseButtons' order

	private const uint MaskLeft = 1 << 0;
	private const uint MaskMiddle = 1 << 1;
	private const uint MaskRight = 1 << 2;
	private const uint MaskBack = 1 << 3;
	private const uint MaskForward = 1 << 4;

	//Modifiers and buttons held by nobody: what an agent driving the editor over MCP says it is holding, read here alongside what the window system says.
	//The window's own events carry their modifiers with them; this is for the widgets that ask the desktop instead, which a pretended key never reaches.
	internal static KeyboardModifiers PretendedModifiers { get; set; }

	internal static MouseButtons PretendedButtons { get; set; }

	/// <summary>Which of shift, control, alt and the windows key are held.</summary>
	public static (bool Shift, bool Ctrl, bool Alt, bool Meta) Modifiers
	{
		get
		{
			var pretended = PretendedModifiers;
			var shift = (pretended & KeyboardModifiers.Shift) != 0;
			var ctrl = (pretended & KeyboardModifiers.Ctrl) != 0;
			var alt = (pretended & KeyboardModifiers.Alt) != 0;

			if ( !Here ) return (shift, ctrl, alt, false);

			var state = SDL_GetModState();

			return (shift || (state & ModShift) != 0, ctrl || (state & ModCtrl) != 0, alt || (state & ModAlt) != 0, (state & ModGui) != 0);
		}
	}

	//As a set of flags rather than as three bools in an order somebody has to remember: a tuple of Left, Middle, Right reads the same as one of Left, Right, Middle at the call site, and the compiler has nothing to say about it.
	/// <summary>Which mouse buttons are held, anywhere on the desktop.</summary>
	public static MouseButtons Buttons
	{
		get
		{
			if ( !Here ) return PretendedButtons;

			var state = SDL_GetGlobalMouseState( out _, out _ );

			var held = PretendedButtons;

			if ( (state & MaskLeft) != 0 ) held |= MouseButtons.Left;
			if ( (state & MaskRight) != 0 ) held |= MouseButtons.Right;
			if ( (state & MaskMiddle) != 0 ) held |= MouseButtons.Middle;
			if ( (state & MaskBack) != 0 ) held |= MouseButtons.Back;
			if ( (state & MaskForward) != 0 ) held |= MouseButtons.Forward;

			return held;
		}
	}

	[LibraryImport( SDL, EntryPoint = "SDL_GetMouseFocus" )]
	private static partial IntPtr SDL_GetMouseFocus();

	[LibraryImport( SDL, EntryPoint = "SDL_GetWindowFlags" )]
	private static partial ulong SDL_GetWindowFlags( IntPtr window );

	//Which window the window system says the pointer is over.
	internal static IntPtr MouseFocus => Here ? SDL_GetMouseFocus() : IntPtr.Zero;

	//What the window system thinks a window is doing.
	internal static ulong FlagsOf( IntPtr window ) => window == IntPtr.Zero ? 0 : SDL_GetWindowFlags( window );

	/// <summary>Whether a key is held, by its SDL scancode.</summary>
	public static bool IsScancodeDown( int scancode )
	{
		if ( !Here || scancode <= 0 ) return false;

		var keys = SDL_GetKeyboardState( out var count );

		if ( keys == IntPtr.Zero || scancode >= count ) return false;

		return Marshal.ReadByte( keys, scancode ) != 0;
	}

	/// <summary>How much bigger than its pixels a window is meant to draw - what the desktop's scaling setting is where the window is.</summary>
	public static float ScaleOf( IntPtr window )
	{
		if ( !Here || window == IntPtr.Zero ) return 1.0f;

		var scale = SDL_GetWindowDisplayScale( window );

		return scale <= 0 ? 1.0f : scale;
	}
}
