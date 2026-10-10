
using Rinten;

//Hidden class. addon code should only ever access AnimationSequence.
internal sealed class SceneObjectAnimationSequence : AnimationSequence
{
	readonly SceneModel Target;

	public SceneObjectAnimationSequence( SceneModel sceneObject )
	{
		Target = sceneObject;
	}

	public override float Duration => Target.Animation.SequenceDuration;

	public override bool IsFinished => Target.Animation.SequenceFinished;

	public override string Name
	{
		get => Target.Animation.Sequence;
		set => Target.Animation.Sequence = value;
	}

	public override float TimeNormalized
	{
		get => Target.Animation.SequenceCycle;
		set => Target.Animation.SequenceCycle = value;
	}

	public override float Time
	{
		get => TimeNormalized * Duration;
		set => TimeNormalized = value / Duration;
	}

	internal override bool Looping
	{
		set => Target.Animation.SequenceLooping = value;
	}

	internal override bool Blending
	{
		set => Target.Animation.SequenceBlending = value;
	}

	public override IReadOnlyList<string> SequenceNames => Target.Animation.SequenceNames;
}
