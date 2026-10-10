namespace Rinten.Diagnostics;

//Only the listening: the rest of Logging - its rules, its levels, its queue - is the engine's, and stays so.
/// <summary>The log, for a game that shows it itself - in a headset, or in a build with no terminal behind it, where it is otherwise nowhere a player can read it.</summary>
public static class LogListener
{
	/// <summary>Every message logged, on the main thread - the same as a console is told.</summary>
	public static event Action<LogEvent> Logged
	{
		add => Logging.OnMessage += value;
		remove => Logging.OnMessage -= value;
	}
}
