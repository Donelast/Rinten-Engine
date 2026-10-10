
namespace Rinten;

public abstract class AnimationSequence
{
	/// <summary>The duration of the currently playing sequence (seconds)</summary>
	public abstract float Duration { get; }

	/// <summary>Get whether the current animation sequence has finished</summary>
	public abstract bool IsFinished { get; }

	/// <summary>The name of the currently playing animation sequence</summary>
	public abstract string Name { get; set; }

	/// <summary>The normalized (between 0 and 1) elapsed time of the currently playing animation sequence</summary>
	public abstract float TimeNormalized { get; set; }

	/// <summary>The elapsed time of the currently playing animation sequence (seconds)</summary>
	public abstract float Time { get; set; }

	//Get or set whether the current animation sequence is looping
	internal abstract bool Looping { set; }

	//Get or set whether animations blend smoothly when transitioning between sequences.
	internal abstract bool Blending { set; }

	/// <summary>The list of sequences that can be used</summary>
	public abstract IReadOnlyList<string> SequenceNames { get; }
}
