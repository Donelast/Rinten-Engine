namespace Rinten;

/// <summary>The application leaving the screen and coming back to it - a phone's home button, a call, the screen locking. Posted to the active scene, on the main thread, by the platforms that have a background to go into.</summary>
public interface IApplicationEvents : ISceneEvent<IApplicationEvents>
{
	/// <summary>The application is going into the background. Nothing ticks, draws or plays until it comes back, and it may never come back: a phone kills what is in the background when it wants the memory, without asking. Anything worth keeping - a save - is written here.</summary>
	void OnApplicationPause() { }

	/// <summary>The application is back in front of the player. The time it was away does not count towards the frame's delta.</summary>
	void OnApplicationResume() { }
}
