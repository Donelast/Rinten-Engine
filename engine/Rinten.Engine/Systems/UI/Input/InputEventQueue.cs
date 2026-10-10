using NativeEngine;

namespace Rinten.UI;


//Queue input events on here to be processed by the UISystem.
class InputEventQueue
{
	Queue<PanelEvent> PanelEvents = new();
	Queue<ButtonEvent> ButtonEvents = new();
	Queue<string> DoubleClicks = new();
	Queue<ButtonEvent> ButtonTyped = new();
	Queue<char> KeyTyped = new();

	Vector2 MouseMovement;

	internal static string NormalizeButtonName( string button )
	{
		button = button.ToLowerInvariant();

		if ( button.StartsWith( "key_" ) )
			button = button[4..];

		return button;
	}

	internal void TickFocused( Panel focused )
	{
		if ( !focused.IsValid() )
		{
			ButtonEvents.Clear();
			KeyTyped.Clear();
			ButtonTyped.Clear();
			PanelEvents.Clear();
			return;
		}

		while ( ButtonEvents.TryDequeue( out var e ) )
		{
			focused.OnButtonEvent( e );
		}

		while ( KeyTyped.TryDequeue( out var e ) )
		{
			focused?.OnKeyTyped( e );
		}

		while ( ButtonTyped.TryDequeue( out var e ) )
		{
			focused?.OnButtonTyped( e );
		}

		while ( PanelEvents.TryDequeue( out var e ) )
		{
			e.Target = focused;
			focused.CreateEvent( e );
		}
	}

	internal void Tick( Panel hovered, Panel active )
	{
		if ( MouseMovement != 0 )
		{
			//If we're pressing down on a panel we send all the mouse move events to that
			var moveRecv = hovered;
			if ( active != null ) moveRecv = active;

			moveRecv?.CreateEvent( new MousePanelEvent( "onmousemove", moveRecv, "none" ) );

			MouseMovement = 0;
		}

		var listSize = DoubleClicks.Count;
		for ( int i = 0; i < listSize; i++ )
			if ( DoubleClicks.TryDequeue( out var e ) )
			{
				hovered?.CreateEvent( new MousePanelEvent( "ondoubleclick", hovered, e ) );
			}
	}

	internal void AddDoubleClick( string button )
	{
		button = NormalizeButtonName( button );
		DoubleClicks.Enqueue( button );
	}

	internal void QueueInputEvent( PanelEvent e )
	{
		PanelEvents.Enqueue( e );
	}

	internal void AddButtonEvent( ButtonCode button, bool down, KeyboardModifiers modifiers )
	{
		var e = new ButtonEvent( button, down, modifiers );
		ButtonEvents.Enqueue( e );
	}

	internal void AddKeyTyped( char c )
	{
		KeyTyped.Enqueue( c );
	}

	internal void AddButtonTyped( ButtonCode button, KeyboardModifiers modifiers )
	{
		var e = new ButtonEvent( button, true, modifiers );
		ButtonTyped.Enqueue( e );
	}

	internal void MouseMoved( Vector2 delta )
	{
		MouseMovement += delta;
	}
}
