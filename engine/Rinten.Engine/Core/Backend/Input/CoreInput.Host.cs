using System.Collections.Concurrent;
using NativeEngine;

namespace Rinten;

//Input pushed by a host (Android touch, keys, IME), drained once a frame; first finger acts as mouse
internal static partial class CoreInput
{
	//What a host can say happened.
	internal enum HostKind : byte
	{
		TouchDown,
		TouchMove,
		TouchUp,
		TouchCancel,
		Key,
		Text,
	}

	//One thing that happened, as the host saw it.
	internal readonly record struct HostEvent( HostKind Kind, int Id, float X, float Y, int Code, bool Down, bool Repeat );

	//What the host has said and the engine has not yet read.
	static readonly ConcurrentQueue<HostEvent> host = new();

	//A finger put down, moved, lifted or taken away. Any thread.
	internal static void HostTouch( HostKind stage, int id, float x, float y )
		=> host.Enqueue( new HostEvent( stage, id, x, y, 0, false, false ) );

	//A key, from a keyboard plugged into the device or the system's own keys. Any thread.
	internal static void HostKey( ButtonCode code, bool down, bool repeat = false )
	{
		if ( code == ButtonCode.BUTTON_CODE_INVALID || code == ButtonCode.KEY_NONE ) return;

		host.Enqueue( new HostEvent( HostKind.Key, 0, 0, 0, (int)code, down, repeat ) );
	}

	//Text typed, by an on-screen keyboard or a real one. Any thread.
	internal static void HostText( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return;

		foreach ( var character in text )
			host.Enqueue( new HostEvent( HostKind.Text, 0, 0, 0, character, true, false ) );
	}

	//Shows or hides the host's on-screen keyboard. Set by the host; asked when a text field takes or
	//loses the keyboard - see SetIMEAllowed. Called on the engine's thread, so it posts to its own.
	internal static Action<bool> HostKeyboard { get; set; }

	//Vibrates the device: how long each step lasts in milliseconds, and how hard, 0 to 255. Set by the host, null where there is no motor to reach.
	internal static Action<long[], int[]> HostVibrate { get; set; }

	//Vibrates the device, a step at a time: each step's length in milliseconds and its strength from 0 to 1.
	internal static void VibrateDevice( ReadOnlySpan<int> milliseconds, ReadOnlySpan<float> strength )
	{
		if ( HostVibrate is not { } vibrate ) return;

		var count = Math.Min( milliseconds.Length, strength.Length );
		if ( count == 0 ) return;

		var timings = new long[count];
		var amplitudes = new int[count];

		for ( var i = 0; i < count; i++ )
		{
			timings[i] = Math.Max( milliseconds[i], 0 );
			amplitudes[i] = (int)MathF.Round( Math.Clamp( strength[i], 0, 1 ) * 255 );
		}

		vibrate( timings, amplitudes );
	}

	//Plays a haptic pattern on the device, its amplitude curve read a step at a time.
	internal static void VibrateDevice( HapticPattern pattern, float lengthScale, float amplitudeScale )
	{
		if ( pattern is null || HostVibrate is null ) return;

		const int step = 20;

		var length = (int)( pattern.Length * lengthScale * 1000 );
		if ( length <= 0 ) return;

		var steps = Math.Clamp( ( length + step - 1 ) / step, 1, 200 );

		Span<int> milliseconds = stackalloc int[steps];
		Span<float> strength = stackalloc float[steps];

		for ( var i = 0; i < steps; i++ )
		{
			pattern.GetValue( ( i + 0.5f ) / steps, out _, out var amplitude );

			milliseconds[i] = length / steps;
			strength[i] = amplitude * amplitudeScale;
		}

		VibrateDevice( milliseconds, strength );
	}

	static readonly List<Touch> touches = new();

	//Every finger this frame - see Input.Touches.
	internal static IReadOnlyList<Touch> Touches => touches;

	//Whether a finger has ever been put down.
	internal static bool TouchSeen { get; private set; }

	//The finger that is the mouse, or -1 while none is.
	static int primary = -1;

	//Whether the finger that is the mouse has been put down and its press not yet given - see TouchDown.
	static bool pressPending;

	//Fingers that went down or came up this frame - see PumpHost.
	static readonly HashSet<int> turned = new();

	//The frame the queue was last read in, so it is read once a frame however often the window is polled.
	static long pumpedFrame = -1;

	//Drains host input in order; at most one down/up per finger per frame so quick taps are not lost
	internal static void PumpHost()
	{
		if ( pumpedFrame == EngineLoop.LoopFrames ) return;
		pumpedFrame = EngineLoop.LoopFrames;

		BeginTouchFrame();
		turned.Clear();

		//Last frame's finger, pressed now that the pointer has been where it is for a frame.
		//It is this frame's turn for that finger: it comes up next frame at the soonest.
		if ( pressPending )
		{
			pressPending = false;
			turned.Add( primary );

			Engine.InputRouter.OnMouseButton( ButtonCode.MouseLeft, true, 0 );
		}

		while ( !pressPending && host.TryPeek( out var e ) )
		{
			if ( e.Kind is HostKind.TouchDown or HostKind.TouchUp or HostKind.TouchCancel && !turned.Add( e.Id ) ) break;

			host.TryDequeue( out _ );

			CoreWindow.MarkInput();

			switch ( e.Kind )
			{
				case HostKind.TouchDown:
					TouchDown( e.Id, e.X, e.Y );
					break;

				case HostKind.TouchMove:
					TouchMove( e.Id, e.X, e.Y );
					break;

				case HostKind.TouchUp:
				case HostKind.TouchCancel:
					TouchUp( e.Id, e.X, e.Y, e.Kind == HostKind.TouchCancel );
					break;

				case HostKind.Key:
					HostKeyEvent( (ButtonCode)e.Code, e.Down, e.Repeat );
					break;

				case HostKind.Text:
					Engine.InputRouter.OnText( (uint)e.Code );
					break;
			}
		}
	}

	//Last frame's lifted fingers go, and the rest have not moved yet.
	static void BeginTouchFrame()
	{
		touches.RemoveAll( static t => !t.IsDown );

		for ( var i = 0; i < touches.Count; i++ )
		{
			touches[i] = touches[i] with { Delta = default, Phase = TouchPhase.Stationary };
		}
	}

	static int FindTouch( int id )
	{
		for ( var i = 0; i < touches.Count; i++ )
		{
			if ( touches[i].Id == id && touches[i].IsDown ) return i;
		}

		return -1;
	}

	//Where a point in the window is on the game's screen - see InputRouter.ScreenOrigin.
	static Vector2 OnScreen( float x, float y ) => new Vector2( x, y ) - ( Engine.InputRouter.ScreenOrigin ?? default );

	static void TouchDown( int id, float x, float y )
	{
		TouchSeen = true;

		//A finger the host never said came up - it is the same number again.
		var stale = FindTouch( id );
		if ( stale >= 0 ) touches.RemoveAt( stale );

		var at = OnScreen( x, y );
		touches.Add( new Touch( id, at, default, at, TouchPhase.Began ) );

		if ( primary >= 0 ) return;

		primary = id;

		//Move now, press next frame, so hover is updated before the press is routed
		if ( !Engine.InputRouter.MouseCaptured )
			Engine.InputRouter.OnMousePositionChange( x, y, 0, 0 );

		pressPending = true;
	}

	static void TouchMove( int id, float x, float y )
	{
		var index = FindTouch( id );
		if ( index < 0 ) return;

		var touch = touches[index];
		var at = OnScreen( x, y );
		var moved = at - touch.Position;

		if ( moved.x == 0 && moved.y == 0 ) return;

		touches[index] = touch with
		{
			Position = at,
			Delta = touch.Delta + moved,
			Phase = touch.Phase == TouchPhase.Began ? TouchPhase.Began : TouchPhase.Moved,
		};

		if ( id != primary ) return;

		//A game holding the mouse - a look, a drag - hears how far it moved, as it would from a mouse with no cursor.
		if ( Engine.InputRouter.MouseCaptured )
			Engine.InputRouter.OnMouseMotion( moved.x, moved.y );
		else
			Engine.InputRouter.OnMousePositionChange( x, y, moved.x, moved.y );
	}

	static void TouchUp( int id, float x, float y, bool cancelled )
	{
		var index = FindTouch( id );

		if ( index >= 0 )
		{
			var touch = touches[index];
			var at = cancelled ? touch.Position : OnScreen( x, y );

			touches[index] = touch with
			{
				Position = at,
				Delta = touch.Delta + ( at - touch.Position ),
				Phase = cancelled ? TouchPhase.Cancelled : TouchPhase.Ended,
			};
		}

		if ( id != primary ) return;

		primary = -1;

		if ( !cancelled && !Engine.InputRouter.MouseCaptured )
			Engine.InputRouter.OnMousePositionChange( x, y, 0, 0 );

		//Never pressed, so nothing to let go of.
		if ( pressPending )
		{
			pressPending = false;
			return;
		}

		Engine.InputRouter.OnMouseButton( ButtonCode.MouseLeft, false, 0 );
	}

	//Every finger taken away - the application is going into the background, and nothing it held may stay held while it is away.
	internal static void CancelTouches()
	{
		host.Clear();

		for ( var i = 0; i < touches.Count; i++ )
		{
			if ( touches[i].IsDown ) touches[i] = touches[i] with { Delta = default, Phase = TouchPhase.Cancelled };
		}

		if ( primary >= 0 )
		{
			primary = -1;

			if ( pressPending ) pressPending = false;
			else Engine.InputRouter.OnMouseButton( ButtonCode.MouseLeft, false, 0 );
		}
	}

	//A key from the host, the way SDL's key events are handed on - see Key.
	static void HostKeyEvent( ButtonCode code, bool down, bool repeat )
	{
		Engine.InputRouter.OnKey( code, code, down, repeat, 0 );
	}

	//What the host's keyboard was last told, so it is told only when that changes.
	static bool? hostKeyboardShown;

	//Shows or hides the host's on-screen keyboard, once per change.
	static void ShowHostKeyboard( bool shown )
	{
		if ( HostKeyboard is not { } keyboard ) return;
		if ( hostKeyboardShown == shown ) return;

		hostKeyboardShown = shown;
		keyboard( shown );
	}
}
