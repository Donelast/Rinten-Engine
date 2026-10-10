using NativeEngine;
using Rinten.Engine;
using System;

namespace Editor.Mcp;

/// <summary>Play mode a frame at a time, and a player's hands: the game run for exactly so many frames, waited on, and given the keys and the mouse a person at it would press. The start, stop and pause tools live in the tools addon; these are the engine's because they reach into what only it holds - the frame step, and the input router.</summary>
[McpToolset( "play" )]
internal static class PlayTools
{
	[McpTool( "play_step" )]
	[Description( "Run the paused game for exactly so many frames, then leave it paused. Pauses it first if it's running. This is how physics and animation are looked at deterministically: step, then execute_code or get_game_object to read the state, then a screenshot, then step again. Returns how much scene time passed." )]
	public static async Task<StepResult> PlayStep(
		[Description( "How many frames to run. Default 1, max 600." ), Range( 1, 600 )] int frames = 1 )
	{
		if ( !Game.IsPlaying )
			throw new Exception( "Not playing - play_start first." );

		var scene = Game.ActiveScene;
		var timeBefore = scene?.TimeNow ?? 0;
		var loopBefore = EngineLoop.LoopFrames;

		Game.IsPaused = true;
		Game.StepFrames = frames;

		//Until the steps are spent and the frame that spent the last one is over
		await Frame.Until( () => Game.StepFrames == 0 && EngineLoop.LoopFrames > loopBefore + frames, TimeSpan.FromSeconds( 60 ),
			() => $"The game hasn't run {frames} frames in 60 seconds - {Game.StepFrames} left. Is the editor drawing?" );

		if ( !Game.IsPlaying )
			throw new Exception( "Play mode ended while stepping." );

		return new StepResult
		{
			Frames = frames,
			SceneTimeBefore = timeBefore,
			SceneTimeAfter = scene?.TimeNow ?? 0,
			IsPaused = Game.IsPaused,
		};
	}

	[McpTool( "play_wait" )]
	[Description( "Wait while the game runs - for so many seconds of real time, or so many engine frames - then return with how much scene time passed. For letting a thing fall, a probe log, an animation reach a point, before reading or shooting it. Doesn't pause anything: play_pause and play_step do." )]
	public static async Task<StepResult> PlayWait(
		[Description( "Seconds of real time to wait. Default 1, max 60." ), Range( 0, 60 )] float seconds = 1,
		[Description( "Engine frames to wait instead, when not 0. Max 3600." ), Range( 0, 3600 )] int frames = 0 )
	{
		var scene = Game.ActiveScene;
		var timeBefore = scene?.TimeNow ?? 0;
		var loopBefore = EngineLoop.LoopFrames;

		if ( frames > 0 )
		{
			await Frame.Until( () => EngineLoop.LoopFrames >= loopBefore + frames, TimeSpan.FromSeconds( 120 ),
				() => $"Only {EngineLoop.LoopFrames - loopBefore} of {frames} frames ran in 120 seconds." );
		}
		else
		{
			await Frame.For( TimeSpan.FromSeconds( seconds ) );
		}

		return new StepResult
		{
			Frames = (int)(EngineLoop.LoopFrames - loopBefore),
			SceneTimeBefore = timeBefore,
			SceneTimeAfter = Game.ActiveScene?.TimeNow ?? 0,
			IsPaused = Game.IsPaused,
		};
	}

	[McpTool( "game_key" )]
	[Description( "Press a key for the game being played, as a player would: an input action by name ('Jump', 'Forward', 'Use' - the project's own, from its input settings) or a key by name ('w', 'space', 'mouseleft'), held for so long, then released. Goes through the game's binds, so what the game does with it is what it does for a person. Movement keys move the player for as long as they're held: hold 'Forward' for 1 second to walk. While playing, the mouse and keyboard are otherwise the editor's." )]
	public static async Task<string> GameKey(
		[Description( "An input action name from the project's input settings, or a key name like 'w', 'space', 'e', 'mouseleft', 'mouseright'." )] string key,
		[Description( "How long to hold it, in seconds. Default 0.15 - a tap. Max 30." ), Range( 0.01f, 30 )] float seconds = 0.15f )
	{
		if ( !Game.IsPlaying )
			throw new Exception( "Not playing - play_start first." );

		var (code, what) = Resolve( key );

		using var playing = await AsThePlayer();

		Press( code, true );

		try
		{
			await Frame.For( TimeSpan.FromSeconds( seconds ) );
		}
		finally
		{
			Press( code, false );
		}

		return $"Held {what} for {seconds:0.##}s. read_console or execute_code shows what the game made of it.";
	}

	[McpTool( "game_look" )]
	[Description( "Move the mouse for the game being played - turn the player's view. A relative motion in pixels, delivered over a few frames the way a hand moves: positive x looks right, positive y looks down. The game scales it by its sensitivity." )]
	public static async Task<string> GameLook(
		[Description( "Pixels to move sideways. Positive is right." )] float x = 0,
		[Description( "Pixels to move up and down. Positive is down." )] float y = 0,
		[Description( "Over how many frames. Default 5, max 120." ), Range( 1, 120 )] int frames = 5 )
	{
		if ( !Game.IsPlaying )
			throw new Exception( "Not playing - play_start first." );

		using var playing = await AsThePlayer();

		for ( var i = 0; i < frames; i++ )
		{
			InputRouter.OnMouseMotion( x / frames, y / frames );
			await Frame.Next();
		}

		return $"Moved the mouse {x:0},{y:0} over {frames} frames.";
	}

	//The game given the keyboard and the mouse for the length of a using, as a click into the game view gives them - and given back
	//after, unless it had them already. An editor with nobody at it has released them: the game only hears keys while it holds them.
	static async Task<IDisposable> AsThePlayer()
	{
		//At the window, as far as the game can tell - the look is dropped otherwise
		CoreInput.MouseFocusPretended = true;

		if ( GameMode.HasInput ) return new GiveBack( release: false );

		GameMode.TakeInput();

		if ( !GameMode.HasInput )
		{
			CoreInput.MouseFocusPretended = false;
			throw new Exception( "The game couldn't be given the input - its view isn't showing. Bring the play tab to the front, or play_stop and play_start." );
		}

		//The game's context learns it has the keyboard on the UI's next tick; a key pressed before that is a key pressed at the editor, and dropped.
		await Frame.Next( 2 );

		return new GiveBack( release: true );
	}

	//What was taken for the call, given back: the input if it was, the pretended focus always.
	sealed class GiveBack( bool release ) : IDisposable
	{
		public void Dispose()
		{
			CoreInput.MouseFocusPretended = false;

			if ( release ) GameMode.ReleaseInput();
		}
	}

	//An action's key, or a key by name, as the router takes it.
	static (ButtonCode Code, string What) Resolve( string key )
	{
		var wanted = key?.Trim() ?? "";

		if ( wanted.Length == 0 )
			throw new Exception( "Give an action or a key." );

		var action = Rinten.Input.InputActions?.FirstOrDefault( x => string.Equals( x.Name, wanted, StringComparison.OrdinalIgnoreCase ) );

		if ( action is not null )
		{
			var bound = Rinten.EngineInput.StringToButtonCode( action.KeyboardCode );

			if ( bound == ButtonCode.BUTTON_CODE_INVALID )
				throw new Exception( $"Action '{action.Name}' is bound to '{action.KeyboardCode}', which isn't a key the engine knows." );

			return (bound, $"'{action.Name}' ({action.KeyboardCode})");
		}

		var code = Rinten.EngineInput.StringToButtonCode( wanted );

		if ( code == ButtonCode.BUTTON_CODE_INVALID )
			code = Rinten.EngineInput.StringToButtonCode( wanted.ToUpperInvariant() );

		if ( code == ButtonCode.BUTTON_CODE_INVALID )
		{
			var actions = string.Join( ", ", Rinten.Input.ActionNames.Take( 30 ) );
			throw new Exception( $"'{wanted}' is neither an action nor a key. The project's actions: {actions}. Keys are letters, digits, space, e, mouseleft, mouseright..." );
		}

		return (code, $"key '{wanted}'");
	}

	static void Press( ButtonCode code, bool down )
	{
		if ( code is ButtonCode.MouseLeft or ButtonCode.MouseRight or ButtonCode.MouseMiddle or ButtonCode.MouseBack or ButtonCode.MouseForward )
			InputRouter.OnMouseButton( code, down, 0 );
		else
			InputRouter.OnKey( code, code, down, repeat: false, 0 );
	}
}

//What play_step and play_wait report.
internal class StepResult
{
	[Description( "Engine frames that ran." )]
	public int Frames { get; set; }
	public double SceneTimeBefore { get; set; }
	public double SceneTimeAfter { get; set; }
	public bool IsPaused { get; set; }
}
