using NativeEngine;
using Rinten.Utility;

namespace Rinten;

public static partial class Input
{
	//The current input context, pushed using Context.Push
	static Context CurrentContext { get; set; }

	//weak list of contexts
	static List<WeakReference<Context>> contexts = new List<WeakReference<Context>>();
	static IDisposable defaultContextScope;

	//Get all of the contexts
	internal static IEnumerable<Context> Contexts
	{
		get
		{
			//remove collected
			contexts.RemoveAll( x => x.TryGetTarget( out _ ) == false );

			foreach ( var t in contexts )
			{
				if ( t.TryGetTarget( out var x ) )
					yield return x;
			}
		}
	}

	static Input()
	{
		//We should always have a context available This will just get GC'd on client as we replace it with frame/tick context
		var d = Context.Create( "Default" );
		defaultContextScope = d.Push();
	}

	/// <summary>If the input is suppressed then everything will act like there is no input</summary>
	public static bool Suppressed { get; set; }


	//Allows tracking states of button changes and input deltas in a custom period (such as a
	//tick) rather than in a per frame manner. This allows frame and tick to have legit data.
	internal class Context
	{
		public string Name { get; private set; }

		//These are accumulated values. We collect these during input, then on Flip we put these into the actual values and reset.
		internal ulong AccumActionsPressed { get; set; }
		internal ulong AccumActionsReleased { get; set; }
		internal Vector2 AccumMouseDelta { get; set; }
		internal Vector2 AccumMouseWheel { get; set; }
		internal HashSet<ButtonCode> AccumKeysPressed { get; set; } = new();
		internal HashSet<ButtonCode> AccumKeysReleased { get; set; } = new();

		//Last tick
		internal ulong SavedActions { get; set; }
		internal HashSet<ButtonCode> SavedKeys { get; set; } = new();

		//The input states for this context. These are accessed via the Input class, when it's been pushed.
		internal Vector2 MouseDelta { get; set; }
		internal Vector2 MouseWheel { get; set; }
		internal ulong ActionsCurrent { get; set; }
		internal ulong ActionsPrevious { get; set; }
		internal HashSet<ButtonCode> KeysCurrent { get; set; } = new();
		internal HashSet<ButtonCode> KeysPrevious { get; set; } = new();
		internal bool MouseCursorVisible { get; set; }

		//A press cancels a pending release, so IME key-repeat (release+press) keeps the action held
		internal void PressAction( int index )
		{
			AccumActionsPressed |= 1UL << index;
			AccumActionsReleased &= ~(1UL << index);
		}

		//An action came up since the last flip.
		internal void ReleaseAction( int index ) => AccumActionsReleased |= 1UL << index;

		//A key went down since the last flip - see PressAction.
		internal void PressKey( ButtonCode code )
		{
			AccumKeysPressed.Add( code );
			AccumKeysReleased.Remove( code );
		}

		//A key came up since the last flip.
		internal void ReleaseKey( ButtonCode code ) => AccumKeysReleased.Add( code );

		public static Context Create( string name )
		{
			var context = new Context( name );
			contexts.Add( new( context ) );
			return context;
		}

		Context( string name )
		{
			this.Name = name;
		}

		/// <summary>Copy accumulated values. Flip previous actions to current actions etc.</summary>
		public void Flip()
		{
			//Accumulate and reset mouse input
			MouseDelta = AccumMouseDelta;
			AccumMouseDelta = default;
			MouseWheel = AccumMouseWheel;
			AccumMouseWheel = default;

			//previous actions are current
			ActionsPrevious = SavedActions;

			//The set that falls out of use this flip - it is filled again below.
			var spare = KeysPrevious;

			KeysPrevious = SavedKeys;

			//Current keys are all pressed
			KeysCurrent = AccumKeysPressed;
			SavedKeys = KeysCurrent;

			//Current actions are all pressed
			ActionsCurrent = AccumActionsPressed;
			SavedActions = ActionsCurrent;

			//Remove released actions
			AccumActionsPressed &= ~AccumActionsReleased;
			AccumActionsReleased = default;

			//Remove released keys - into the set nothing reads any more rather than a new one: Except and ToHashSet were an iterator and a set for every
			//context, every flip. Three sets turn over between previous, current and pressed, and the one previous leaves behind is the one no reader holds.
			spare.Clear();
			spare.UnionWith( KeysCurrent );
			spare.ExceptWith( AccumKeysReleased );
			AccumKeysPressed = spare;
			AccumKeysReleased.Clear();
		}

		/// <summary>Make this the current active context. You can optionally use the returned IDisposable to restore back to the previous context when you're done.</summary>
		public IDisposable Push() => PushScope();

		//Push without boxing the way back: the scene's fixed update pushes a context every step,
		//and the closure, the delegate and the box were three objects a step for a pointer swap.
		internal ContextScope PushScope()
		{
			var was = CurrentContext;

			MakeCurrent();

			return new ContextScope( was );
		}

		//Makes this the current context and nothing else - for a caller that never goes back.
		internal void MakeCurrent() => CurrentContext = this;

		//Puts back the context that was current before a PushScope.
		internal readonly struct ContextScope : IDisposable
		{
			private readonly Context was;

			internal ContextScope( Context was ) => this.was = was;

			public void Dispose() => was?.MakeCurrent();
		}
	}
}
