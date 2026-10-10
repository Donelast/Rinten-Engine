using System.Runtime.InteropServices;
using NativeEngine;

namespace Rinten;

//Gamepads on SDL3; button and axis enums match SDL's values, so mapping is a cast
internal static unsafe partial class CoreInput
{
	private const uint SDL_INIT_GAMEPAD = 0x00002000u;

	private const uint SDL_EVENT_GAMEPAD_AXIS_MOTION = 0x650;
	private const uint SDL_EVENT_GAMEPAD_BUTTON_DOWN = 0x651;
	private const uint SDL_EVENT_GAMEPAD_BUTTON_UP = 0x652;
	private const uint SDL_EVENT_GAMEPAD_ADDED = 0x653;
	private const uint SDL_EVENT_GAMEPAD_REMOVED = 0x654;

	[LibraryImport( SDL, EntryPoint = "SDL_InitSubSystem" )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_InitSubSystemGamepad( uint flags );

	[LibraryImport( SDL, EntryPoint = "SDL_GetGamepads" )]
	private static partial uint* SDL_GetGamepads( out int count );

	[LibraryImport( SDL, EntryPoint = "SDL_OpenGamepad" )]
	private static partial IntPtr SDL_OpenGamepad( uint instanceId );

	[LibraryImport( SDL, EntryPoint = "SDL_CloseGamepad" )]
	private static partial void SDL_CloseGamepad( IntPtr gamepad );

	[LibraryImport( SDL, EntryPoint = "SDL_GetGamepadName" )]
	private static partial IntPtr SDL_GetGamepadName( IntPtr gamepad );

	[LibraryImport( SDL, EntryPoint = "SDL_GetGamepadType" )]
	private static partial int SDL_GetGamepadType( IntPtr gamepad );

	[LibraryImport( SDL, EntryPoint = "SDL_RumbleGamepad" )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_RumbleGamepad( IntPtr gamepad, ushort low, ushort high, uint durationMs );

	[LibraryImport( SDL, EntryPoint = "SDL_RumbleGamepadTriggers" )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_RumbleGamepadTriggers( IntPtr gamepad, ushort left, ushort right, uint durationMs );

	[LibraryImport( SDL, EntryPoint = "SDL_SetGamepadLED" )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_SetGamepadLED( IntPtr gamepad, byte red, byte green, byte blue );

	[LibraryImport( SDL, EntryPoint = "SDL_free" )]
	private static partial void SDL_free( void* memory );

	private static bool gamepadsReady;

	//Start the gamepad subsystem and take whatever is already plugged in. Safe to call more than once, and safe to call where there
	//is no SDL at all: Android and the web have none, and every call here answers false rather than throwing - see IPlatformBackend.
	internal static void StartGamepads()
	{
		if ( gamepadsReady ) return;

		try
		{
			if ( !SDL_InitSubSystemGamepad( SDL_INIT_GAMEPAD ) )
			{
				Log.Info( "Gamepads: SDL would not start its gamepad subsystem." );
				return;
			}

			gamepadsReady = true;

			//What was plugged in before we asked. Anything after this arrives as an event, which is the same path with one fewer special case.
			var ids = SDL_GetGamepads( out var count );
			if ( ids is null ) return;

			for ( var i = 0; i < count; i++ ) Added( ids[i] );

			SDL_free( ids );
		}
		catch ( DllNotFoundException )
		{
			//No SDL here, which is a platform rather than a fault.
		}
		catch ( EntryPointNotFoundException )
		{
		}
	}

	//Close everything, for a clean shutdown.
	internal static void StopGamepads()
	{
		foreach ( var controller in Controller.All.ToArray() ) Removed( (uint)controller.DeviceId );

		gamepadsReady = false;
	}

	private static void Added( uint instanceId )
	{
		if ( Controller.All.Any( x => x.DeviceId == (int)instanceId ) ) return;

		var gamepad = SDL_OpenGamepad( instanceId );
		if ( gamepad == IntPtr.Zero ) return;

		var name = Marshal.PtrToStringUTF8( SDL_GetGamepadName( gamepad ) ) ?? "Gamepad";

		var controller = new Controller( (int)instanceId, (int)instanceId )
		{
			Name = name,
			GlyphSet = GlyphSetOf( SDL_GetGamepadType( gamepad ) ),
			Handle = gamepad,
		};

		Controller.All.Add( controller );

		Log.Info( $"Gamepad connected: {name}" );
	}

	private static void Removed( uint instanceId )
	{
		var controller = Controller.All.FirstOrDefault( x => x.DeviceId == (int)instanceId );
		if ( controller is null ) return;

		Controller.All.Remove( controller );

		if ( controller.Handle != IntPtr.Zero ) SDL_CloseGamepad( controller.Handle );

		Log.Info( $"Gamepad disconnected: {controller.Name}" );
	}

	//Which glyphs to draw for a pad, from what SDL says it is. The numbers are SDL_GamepadType: 1 standard, 2 Xbox 360, 3 Xbox One, 4 PS3, 5 PS4, 6 PS5, 7
	//Switch Pro, 8 and 9 Joy-Cons. Anything else gets Xbox, because a pad we do not recognise is far likelier to be laid out like one than like a Switch.
	private static GameControllerGlyphSet GlyphSetOf( int type ) => type switch
	{
		2 or 3 => GameControllerGlyphSet.Xbox,
		4 or 5 or 6 => GameControllerGlyphSet.PlayStation,
		7 or 8 or 9 => GameControllerGlyphSet.Switch,
		_ => GameControllerGlyphSet.Xbox,
	};

	//One gamepad event. Read by offset like every other event here. All of them share the first sixteen bytes - number, padding, timestamp - so the
	//joystick id is always at sixteen; a button is at twenty with its state at twenty one, and an axis is at twenty with its value at twenty four.
	private static void Gamepad( byte* raw, uint type )
	{
		var which = *(uint*)( raw + 16 );

		switch ( type )
		{
			case SDL_EVENT_GAMEPAD_ADDED:
				Added( which );
				break;

			case SDL_EVENT_GAMEPAD_REMOVED:
				Removed( which );
				break;

			case SDL_EVENT_GAMEPAD_BUTTON_DOWN:
			case SDL_EVENT_GAMEPAD_BUTTON_UP:
			{
				var button = *( raw + 20 );

				//Through the router's own door rather than past it: it maps the code, sets the
				//button state and walks the input actions, and all of that is its business.
				Engine.InputRouter.OnGameControllerButton( (int)which, (GameControllerCode)button,
					type == SDL_EVENT_GAMEPAD_BUTTON_DOWN );
				break;
			}

			case SDL_EVENT_GAMEPAD_AXIS_MOTION:
			{
				var axis = *( raw + 20 );
				var value = *(short*)( raw + 24 );

				var controller = Controller.All.FirstOrDefault( x => x.DeviceId == (int)which );

				//The deadzone and the normalising are the controller's, which already knew how to do both and had nobody calling it.
				controller?.SetAxis( (GameControllerAxis)axis, value );
				break;
			}
		}
	}

	//-- what the controller asks of us ---------------------------------------

	internal static void Rumble( IntPtr gamepad, int left, int right, int duration )
	{
		if ( gamepad == IntPtr.Zero ) return;

		SDL_RumbleGamepad( gamepad, Motor( left ), Motor( right ), (uint)Math.Max( duration, 0 ) );
	}

	internal static void RumbleTriggers( IntPtr gamepad, int left, int right, int duration )
	{
		if ( gamepad == IntPtr.Zero ) return;

		SDL_RumbleGamepadTriggers( gamepad, Motor( left ), Motor( right ), (uint)Math.Max( duration, 0 ) );
	}

	internal static bool SetLed( IntPtr gamepad, Color32 colour )
	{
		if ( gamepad == IntPtr.Zero ) return false;

		return SDL_SetGamepadLED( gamepad, colour.r, colour.g, colour.b );
	}

	//A motor speed, clamped to what SDL takes.
	private static ushort Motor( int value ) => (ushort)Math.Clamp( value, 0, ushort.MaxValue );
}
