using Rinten;

//Hidden class, addon code should only access DirectPlayback. Plays a clip by name, no graph
internal sealed class SceneObjectDirectPlayback : AnimGraphDirectPlayback
{
	readonly SceneModel Target;

	public SceneObjectDirectPlayback( SceneModel sceneObject )
	{
		Target = sceneObject;
	}

	/// <summary>Where in the clip it starts, as a fraction. Set rather than remembered: starting somewhere is a jump to that point.</summary>
	public override float StartTime
	{
		set
		{
			var duration = Duration;

			Target.Animation.SequenceCycle = duration > 0.0f ? value / duration : 0.0f;
		}
	}

	public override float TimeNormalized => Target.Animation.SequenceCycle;

	public override float Duration => Target.Animation.SequenceDuration;

	public override float Time => TimeNormalized * Duration;

	public override string Name => Target.Animation.Sequence;

	[Obsolete]
	public override int AnimationCount => Target.Model != null ? Target.Model.AnimationCount : 0;

	[Obsolete]
	public override IEnumerable<string> Animations => Target.Model is null ? Enumerable.Empty<string>() : Target.Model.AnimationNames;

	public override IReadOnlyList<string> Sequences => Target.Model is null ? Array.Empty<string>() : Target.Model.SequenceNames;

	public override void Play( string name )
	{
		Target.Animation.Sequence = name;
	}

	/// <summary>The same, aimed somewhere. What the aim is for - a clip that walks to a point, blended in over a moment - belongs to a graph rather than to playing one clip, so the aim is ignored and the clip plays.</summary>
	public override void Play( string name, Vector3 target, float heading, float interpTime )
	{
		Target.Animation.Sequence = name;
	}

	public override void Cancel()
	{
		Target.Animation.Sequence = null;
	}
}
