using NativeEngine;
using Rinten.Utility;

namespace Rinten;

public class Time
{
	/// <summary>The time since the game startup.</summary>
	public static float Now { get; set; }

	/// <summary>The delta between the last frame and the current (for all intents and purposes).</summary>
	public static float Delta { get; set; }

	/// <summary>The time since the game startup as a double.</summary>
	public static double NowDouble { get; set; }

	//Audio.Time , Audio.TimeDelta - if these are needed

	//public static double Sound => g_pSoundSystem.AudioStateHostTime();
	//public static double SoundDelta => g_pSoundSystem.AudioStateFrameTime();

	internal static void Update( double now, double delta )
	{
		Now = (float)now;
		Delta = (float)delta;
		NowDouble = now;

		SyncSceneSystemTime();
	}

	public static IDisposable Scope( double now, double delta ) => ScopeInternal( now, delta );

	//Scope without the closure, the delegate and the box: the scene's tick opens one of these every frame and the fixed update one every step.
	internal static TimeScope ScopeInternal( double now, double delta )
	{
		var was = new TimeScope( NowDouble, Delta, Now );

		Update( now, delta );

		return was;
	}

	//The clock as it was before a ScopeInternal, put back on dispose.
	internal readonly struct TimeScope : IDisposable
	{
		private readonly double nowDouble;
		private readonly float delta;
		private readonly float now;

		internal TimeScope( double nowDouble, float delta, float now )
		{
			this.nowDouble = nowDouble;
			this.delta = delta;
			this.now = now;
		}

		public void Dispose()
		{
			NowDouble = nowDouble;
			Delta = delta;
			Now = now;

			SyncSceneSystemTime();
		}
	}

	private static void SyncSceneSystemTime()
	{
		if ( Application.IsUnitTest ) return;

		//Their scene system keeps the time its shaders read. Ours takes it from the frame block, which is written every frame from this same clock.
		return;
	}
}
