using NativeEngine;
using Rinten.Engine;

namespace Rinten;

public static partial class Input
{
	/// <summary>Was the last button pressed a game controller button?</summary>
	public static bool UsingController { get; internal set; } = false;

	internal static ulong Actions
	{
		get => CurrentPlayerScope switch
		{
			0 => CurrentContext.ActionsCurrent,
			> 0 => CurrentController?.InputContext?.ActionsCurrent ?? 0,
			_ => CurrentContext.ActionsCurrent | (Controller.First?.InputContext?.ActionsCurrent ?? 0)
		};
		set => CurrentContext.ActionsCurrent = value;
	}

	static ulong LastActions
	{
		get => CurrentPlayerScope switch
		{
			0 => CurrentContext.ActionsPrevious,
			> 0 => CurrentController?.InputContext?.ActionsPrevious ?? 0,
			_ => CurrentContext.ActionsPrevious | (Controller.First?.InputContext?.ActionsPrevious ?? 0)
		};
		set => CurrentContext.ActionsPrevious = value;
	}

	//Missing action names that we've warned about already. This gets cleared when actions are initialized again.
	private static HashSet<string> MissingActions { get; } = new( StringComparer.OrdinalIgnoreCase );

	//We pack actions bit-by-bit in CUserCmd, using the index (which is shared between realms)
	//to map it to an action. This is an accessor to grab that index easily from its action.
	internal static int GetActionIndex( string actionName )
	{
		//A loop, not FindIndex with a lambda: the lambda captures the name, so every action asked about was
		//a closure and a delegate - four a frame for the analog move alone, before any game asked for one.
		var actions = InputActions;
		if ( actions is null ) return -1;

		for ( var i = 0; i < actions.Count; i++ )
		{
			if ( string.Equals( actions[i].Name, actionName, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return -1;
	}

	internal static int GetActionIndex( InputAction action )
	{
		return InputActions?.IndexOf( action ) ?? -1;
	}

	/// <summary>True if escape was pressed</summary>
	public static bool EscapePressed
	{
		get => InputRouter.EscapeWasPressed;
		set => InputRouter.EscapeWasPressed = value;
	}

	/// <summary>Action is currently pressed down</summary>
	[ActionGraphNode( "input.down" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Down( [InputAction] string action, bool complainOnMissing = true )
	{
		if ( Application.IsHeadless ) return false;
		if ( Suppressed ) return false;
		if ( string.IsNullOrWhiteSpace( action ) ) return false;

		var index = GetActionIndex( action );
		if ( index == -1 )
		{
			if ( complainOnMissing && MissingActions.Add( action ) )
				Log.Warning( $"Couldn't find Input Action called \"{action}\"" );

			return false;
		}

		return (Actions & 1UL << index) != 0;
	}

	internal static bool WasDownLastCommand( string action )
	{
		if ( Suppressed ) return false;
		if ( string.IsNullOrWhiteSpace( action ) ) return false;

		var index = GetActionIndex( action );
		if ( index == -1 && MissingActions.Add( action ) )
		{
			Log.Warning( $"Couldn't find Input Action called \"{action}\"" );
			return false;
		}

		return (LastActions & (1UL << index)) != 0;
	}

	/// <summary>Action wasn't pressed but now it is</summary>
	[ActionGraphNode( "input.pressed" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Pressed( [InputAction] string action )
	{
		if ( Application.IsHeadless ) return false;
		if ( Suppressed ) return false;
		return !WasDownLastCommand( action ) && Down( action );
	}

	/// <summary>Action was pressed but now it isn't</summary>
	[ActionGraphNode( "input.released" ), Pure, Category( "Input" ), Icon( "gamepad" )]
	public static bool Released( [InputAction] string action )
	{
		if ( Application.IsHeadless ) return false;
		if ( Suppressed ) return false;
		return WasDownLastCommand( action ) && !Down( action );
	}

	internal static void SetAction( InputAction action, bool down ) => SetAction( GetActionIndex( action ), down );

	public static void SetAction( string action, bool down ) => SetAction( GetActionIndex( action ), down );

	public static void SetLastAction( string action, bool down ) => SetLastAction( GetActionIndex( action ), down );

	/// <summary>Remove this action, so it's no longer being pressed.</summary>
	public static void Clear( string action ) => SetAction( action, false );

	/// <summary>Clears the current input actions, so that none of them are active.</summary>
	public static void ClearActions()
	{
		Actions = default;
		LastActions = default;
	}

	/// <summary>Clears the current input actions, so that none of them are active. Unlike ClearActions this will unpress the buttons, so they won't be active again until they're pressed again.</summary>
	public static void ReleaseActions()
	{
		foreach ( var e in Contexts )
		{
			e.ActionsCurrent = default;
			e.ActionsPrevious = default;
			e.AccumActionsPressed = default;
		}
	}

	/// <summary>Releases the action, and it won't be active again until it's pressed again.</summary>
	public static void ReleaseAction( string name )
	{
		var index = GetActionIndex( name );

		foreach ( var e in Contexts )
		{
			e.ActionsCurrent &= ~(1UL << index);
			e.ActionsPrevious &= ~(1UL << index);
			e.AccumActionsPressed &= ~(1UL << index);
		}
	}

	//Activates / Deactivates an action when building input.
	internal static void SetAction( int index, bool down )
	{
		if ( down ) Actions |= 1UL << index;
		else Actions &= ~(1UL << index);
	}

	//Activates / Deactivates the previous action when building input.
	internal static void SetLastAction( int index, bool down )
	{
		if ( down ) LastActions |= 1UL << index;
		else LastActions &= ~(1UL << index);
	}

	static InputAction FindInputActionByName( string action )
	{
		return InputActions?.FirstOrDefault( x => string.Equals( x.Name, action, StringComparison.OrdinalIgnoreCase ) );
	}

	static HashSet<string> activeButtons = new HashSet<string>();

	//Called when a compatible button is pressed.
	internal static void OnButton( ButtonCode code, string button, bool down )
	{
		if ( InputActions == null )
		{
			activeButtons.Clear();
			return;
		}

		string loadedGame = Application.GameIdent;

		if ( string.IsNullOrEmpty( loadedGame ) ) loadedGame = "common";

		var collection = InputBinds.FindCollection( loadedGame );

		if ( down )
		{
			activeButtons.Add( button );

			foreach ( var e in Contexts )
			{
				e.PressKey( code );
			}
		}
		else
		{
			foreach ( var e in Contexts )
			{
				e.ReleaseKey( code );
			}

			//remove it but if it wasn't even active, ignore it
			if ( !activeButtons.Remove( button ) )
				return;
		}

		UsingController = false;

		bool handled = false;

		//Find any actions that contain this button
		foreach ( var bind in collection.EnumerateWithButton( button ) )
		{
			var action = FindInputActionByName( bind.Name );

			if ( action == null ) continue;

			//For the action to be active we need a bind with this button and all the other buttons in the bind
			if ( down && !bind.Test( button, activeButtons ) )
				continue;

			//One of the binds for this action passed - so don't do anything
			if ( !down && bind.Test( button, activeButtons ) )
				continue;

			var i = GetActionIndex( action );

			if ( down )
			{
				foreach ( var e in Contexts )
				{
					if ( IsControllerContext( e ) )
						continue;

					e.PressAction( i );
				}
			}
			else
			{
				foreach ( var e in Contexts )
				{
					if ( IsControllerContext( e ) )
						continue;

					e.ReleaseAction( i );
				}
			}

			handled = true;
		}

		if ( !handled )
		{
			OnUnhandledButton( code, button, down );
		}
	}

	static void OnUnhandledButton( ButtonCode code, string button, bool down )
	{
		if ( !down ) return;

		var binding = EngineInput.GetBinding( code );
		if ( string.IsNullOrEmpty( binding ) ) return;

		ConVarSystem.Run( $"{binding}\n" );
	}

	//Returns true if the given context belongs to a controller
	private static bool IsControllerContext( Context context )
	{
		//Not Any - the predicate would capture context and allocate a closure every call.
		foreach ( var c in Controller.All )
		{
			if ( c.InputContext == context ) return true;
		}

		return false;
	}

	internal static InputSettings InputSettings { get; set; }

	internal static List<InputAction> InputActions => InputSettings?.Actions;

	/// <summary>Copies all input actions to be used publicly</summary>
	public static IEnumerable<InputAction> GetActions() => InputActions.Select( x => new InputAction( x.Name, x.KeyboardCode, x.GamepadCode, x.GroupName, x.Title ) );

	/// <summary>Names of all actions from the current game's input settings.</summary>
	public static IEnumerable<string> ActionNames => InputActions?.Select( x => x.Name ) ?? Array.Empty<string>();

	/// <summary>Finds the <see cref="InputAction.GroupName"/> of the given action.</summary>
	public static string GetGroupName( string action ) => FindInputActionByName( action )?.GroupName;

	//Read the config from this source
	internal static void ReadConfig( InputSettings inputConfig )
	{
		InputSettings = inputConfig;

		InputSettings ??= new InputSettings();

		//if there's nothing in the list, we want to init to the defaults
		if ( InputSettings?.Actions?.Count == 0 )
		{
			InputSettings.InitDefault();
		}

		MissingActions.Clear();

		if ( InputActions is null ) return;

		//Fall back to "common" when there's no game ident yet, so binds still learn about the actions
		var ident = Application.GameIdent;
		if ( string.IsNullOrEmpty( ident ) ) ident = "common";

		//Tell the binding system about the new binds so it can set defaults properly
		var collection = InputBinds.FindCollection( ident );
		collection.UpdateActions( InputActions );
	}

	//Three different faults read the same from a chair: the project's actions were never loaded, they were loaded and bound to nothing, or they are bound and the window the keys are going to is not this one. Each looks different here.
	/// <summary>Says what the game's actions are and what they are bound to, for when none of them appear to do anything.</summary>
	[ConCmd( "input_actions", Help = "List the game's input actions, their keys, and which are down" )]
	public static void PrintActions()
	{
		var ident = Application.GameIdent;

		Log.Info( $"input: game '{( string.IsNullOrEmpty( ident ) ? "(none yet)" : ident )}', " +
			$"{InputActions?.Count ?? 0} action(s) loaded" );

		if ( InputActions is null || InputActions.Count == 0 )
		{
			Log.Info( "  Nothing is loaded, so every Input.Down is false. The project's " +
				"ProjectSettings/Input.config is what fills this." );
			return;
		}

		var binds = InputBinds.FindCollection( string.IsNullOrEmpty( ident ) ? "common" : ident );

		foreach ( var action in InputActions )
		{
			var bound = binds is not null && binds.Actions.TryGetValue( action.Name, out var bind ) && bind is not null
				? string.Join( ", ", bind.Slots.Select( x => x.FullString ).Where( x => !string.IsNullOrEmpty( x ) ) )
				: "";

			Log.Info( $"  {action.Name,-16} key {( string.IsNullOrEmpty( action.KeyboardCode ) ? "-" : action.KeyboardCode ),-10} " +
				$"bind {( bound.Length == 0 ? "-" : bound ),-14} {( Down( action.Name ) ? "DOWN" : "" )}" );
		}

		if ( MissingActions.Count > 0 )
			Log.Info( $"  asked for and not found: {string.Join( ", ", MissingActions )}" );
	}
}
