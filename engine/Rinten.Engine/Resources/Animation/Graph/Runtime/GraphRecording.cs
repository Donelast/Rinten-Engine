using Rinten.CoreAnimation;

namespace Rinten.Animation;

//Editor-only recording of each update's output pose, for stepping back through frames
internal sealed class GraphRecording
{
	private readonly List<Update> updates = new();

	private struct Update
	{
		public AnimGraphRecordedUpdateInfo Info;

		/// <summary>The pose it produced, as the runtime keeps a pose.</summary>
		public AnimTrs[] Pose;
		public int Bones;

		/// <summary>Where the thing being posed was standing at the time.</summary>
		public Transform World;
	}

	public bool IsRecording { get; private set; }

	public bool IsPlaying { get; private set; }

	public int Count => updates.Count;

	/// <summary>Which recorded update is being shown, while one is.</summary>
	public int Current { get; private set; }

	public void Start()
	{
		updates.Clear();

		Current = 0;
		IsRecording = true;
		IsPlaying = false;
	}

	public void Stop() => IsRecording = false;

	public bool StartPlayback()
	{
		if ( IsRecording || updates.Count == 0 ) return false;

		IsPlaying = true;
		Current = 0;

		return true;
	}

	public void StopPlayback() => IsPlaying = false;

	public bool TryGetInfo( int update, out AnimGraphRecordedUpdateInfo info )
	{
		info = default;

		if ( update < 0 || update >= updates.Count ) return false;

		info = updates[update].Info;

		return true;
	}

	public Transform WorldOf( int update )
		=> update >= 0 && update < updates.Count ? updates[update].World : Transform.Zero;

	//Taken while the pose still exists, which is inside the update rather than after it: the working pose is handed back at the end of the frame.
	/// <summary>Keep what this update produced.</summary>
	public void Take( AnimGraphInstance graph, int pose, float delta, Transform world )
	{
		if ( !IsRecording || pose < 0 ) return;

		if ( graph.Instance?.Pose( pose ) is not { Length: > 0 } taken ) return;

		var values = taken.AsSpan().ToArray();
		var bones = values.Length;

		//Where the animation had got to, said in its own marks: a recording is stepped through by
		//eye, and "the frame the left foot came down" is how the frame that is wanted gets described.
		var start = updates.Count > 0
			? (updates[^1].Info.EndEvent, updates[^1].Info.EndPercentage)
			: (-1, 0.0f);

		var sync = graph.CurrentSync();

		updates.Add( new Update
		{
			Info = new AnimGraphRecordedUpdateInfo
			{
				Type = delta > 0.0f
					? AnimGraphRecordedUpdateType.TimeStep
					: AnimGraphRecordedUpdateType.ParameterChange,

				DeltaTime = delta,

				StartEvent = start.Item1,
				StartPercentage = start.Item2,

				EndEvent = sync.Index,
				EndPercentage = sync.Fraction,
			},

			Pose = values,
			Bones = bones,
			World = world,
		} );
	}

	/// <summary>Put the model back in a recorded update.</summary>
	public bool Apply( AnimGraphInstance graph, int update )
	{
		if ( update < 0 || update >= updates.Count ) return false;

		var recorded = updates[update];

		var instance = graph.Instance;

		var pose = CoreAnimRuntime.PoseAlloc( instance );
		if ( pose < 0 ) return false;

		try
		{
			//A recording can be shorter than the skeleton it is replayed on - it was taken from whatever was posed at the time.
			if ( instance.Pose( pose ) is { } target )
			{
				var count = Math.Min( recorded.Bones, target.Length );
				recorded.Pose.AsSpan( 0, count ).CopyTo( target );
			}

			CoreAnimRuntime.PoseApply( instance, pose );
		}
		finally
		{
			CoreAnimRuntime.PoseFree( instance, pose );
		}

		Current = update;

		return true;
	}
}
