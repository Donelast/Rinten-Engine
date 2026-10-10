namespace Rinten.Audio;

/// <summary>Audio processor that allows per listener state.</summary>
public abstract class AudioProcessor<T> : AudioProcessor where T : AudioProcessor.ListenerState, new()
{
	//Per listener states.
	private Dictionary<Listener, T> _states;

	//Current state for listener.
	protected T CurrentState { get; private set; }

	//Set current state for listener.
	internal override void SetListener( Listener listener )
	{
		//Try get state and set it as current state.
		if ( _states?.TryGetValue( listener, out T state ) == true )
		{
			CurrentState = state;

			return;
		}

		//New current state.
		CurrentState = new T();

		_states ??= new( ReferenceEqualityComparer.Instance );
		_states[listener] = CurrentState;
	}

	//Remove any states associated with these listeners.
	internal override void RemoveListeners( IReadOnlyList<Listener> listeners )
	{
		if ( _states is null )
			return;

		foreach ( var listener in listeners )
		{
			//Remove and destroy state if it exists.
			if ( _states.Remove( listener, out var data ) )
			{
				data.Destroy();
			}
		}
	}

	//Destroy and clear all states.
	internal override void OnRemovedInternal()
	{
		base.OnRemovedInternal();

		if ( _states is null )
			return;

		//Destroy all states.
		foreach ( var data in _states.Values )
		{
			data.Destroy();
		}

		_states.Clear();
		_states = default;
	}
}

partial class AudioProcessor
{
	/// <summary>One of these is created for every listener that uses an audio processor.</summary>
	public abstract class ListenerState
	{
		internal void Destroy()
		{
			OnDestroy();
		}

		//Called when audio processor or the listener is removed.
		protected virtual void OnDestroy()
		{
		}
	}

	//Optionally target a listener, this processor will only run for a specific listener.
	internal Listener TargetListener { get; set; }

	//Set current state for listener.
	internal virtual void SetListener( Listener listener )
	{
	}

	//Remove any states associated with these listeners.
	internal virtual void RemoveListeners( IReadOnlyList<Listener> listeners )
	{
	}
}
