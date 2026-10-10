using Rinten.CoreAnimation;

namespace Rinten.Animation;

//What a node can reach while a graph is being evaluated: the skeleton it is posing, somewhere to put a pose, and how much time has passed.
//Passed down rather than held by each node, because the same graph runs on every character playing it and none of this belongs to the graph.
internal sealed class GraphContext
{
	private readonly CoreAnimRuntime.Instance instance;

	//Working poses handed out this frame, so none is left behind.
	private readonly List<int> borrowed = new();

	private readonly List<int> borrowedMasks = new();

	public GraphContext( AnimGraphInstance graph, CoreAnimRuntime.Instance instance,
		CoreAnimRuntime.Skeleton skeleton, Model model )
	{
		Graph = graph;
		this.instance = instance;

		Skeleton = skeleton;
		Model = model;
	}

	public AnimGraphInstance Graph { get; }

	//Most of what a node does goes through the methods here, which keep track of what was borrowed. Reading or writing a single bone does not borrow anything, so there is nothing to keep track of.
	/// <summary>What is being posed, for the few things that talk to the core directly.</summary>
	public CoreAnimRuntime.Instance Instance => instance;

	/// <summary>Whoever is watching the graph run, or null - which is everything that is not an editor.</summary>
	public GraphDebug Debug { get; set; }

	/// <summary>The skeleton being posed, for looking a bone up by name.</summary>
	public CoreAnimRuntime.Skeleton Skeleton { get; }

	/// <summary>What the graph is playing on, for finding a clip by name.</summary>
	public Model Model { get; }

	//A pose is in the model's own space; a target the game gives - somewhere to put a hand, somewhere to step - is usually in the world's. This is what stands between the two.
	/// <summary>Where the thing being posed is standing.</summary>
	public Transform World { get; set; } = Transform.Zero;

	/// <summary>A world transform in the space a pose is in.</summary>
	public Transform ToModel( in Transform world ) => World.ToLocal( world );

	/// <summary>The state machine whose transition is being decided right now, if one is.</summary>
	public StateMachineNode Machine { get; set; }

	public string GraphName => Graph?.Name ?? "graph";

	/// <summary>How long since the last update.</summary>
	public float Delta { get; private set; }

	/// <summary>Speed up or slow down everything asked for from here down.</summary>
	public void Scale( float scale ) => Delta *= scale;

	/// <summary>Put it back, which the node that scaled it must always do.</summary>
	public void Restore( float delta ) => Delta = delta;

	/// <summary>Counted up once per update. What a node compares against to know whether the value it worked out is this frame's or the last one's.</summary>
	public int Frame { get; private set; }

	public void Begin( float delta )
	{
		Delta = delta;
		Frame++;

		//What was marked last frame becomes what can be asked about this one, and this frame's marks are collected into the list that was read.
		( said, sampled ) = ( sampled, said );
		sampled.Clear();

		Weight = 1.0f;
		Machine = null;
		State = -1;
	}

	//-- what the animation is saying ----------------------------------------- A clip carries more than a pose. As it plays, whatever the artist marked
	//on it is "sampled" - the playhead is inside it - and a graph reads that to react to its own animation rather than only to what the game tells it.

	/// <summary>One thing a playing clip is in the middle of.</summary>
	public readonly record struct Sampled(
		string Id,
		SampledKind Kind,
		int Data,
		float Fraction,
		float Weight,
		AnimClipData Clip,
		int Index,
		StateMachineNode Machine,
		int State );

	/// <summary>What a sampled event is about.</summary>
	public enum SampledKind
	{
		Generic = 0,
		Foot = 1,
		Sync = 2,
		Curve = 3,
		Transition = 4,

		/// <summary>Raised by the graph rather than read off a clip - a state entered, left, or arrived in.</summary>
		Graph = 5,

		/// <summary>A name that is true for a while: "reloading", "can_be_hit". Read off the clip like the rest, and told to the game as it starts and stops.</summary>
		Tag = 6,
	}

	private List<Sampled> sampled = new();
	private List<Sampled> said = new();

	/// <summary>What the animation was in the middle of when it was last posed.</summary>
	public IReadOnlyList<Sampled> Events => said;

	public void Raise( in Sampled marked ) => sampled.Add( marked );

	/// <summary>How much of the final pose whatever is being evaluated will account for.</summary>
	public float Weight { get; private set; } = 1.0f;

	public float PushWeight( float weight )
	{
		var was = Weight;
		Weight *= weight;

		return was;
	}

	public void PopWeight( float was ) => Weight = was;

	/// <summary>Which state of which machine is being posed, so an event can be told apart from one another state marked.</summary>
	public int State { get; private set; } = -1;

	public (StateMachineNode Machine, int State) PushState( StateMachineNode machine, int state )
	{
		var was = (Machine, State);

		Machine = machine;
		State = state;

		return was;
	}

	public void PopState( (StateMachineNode Machine, int State) was )
	{
		Machine = was.Machine;
		State = was.State;
	}

	//-- working poses --------------------------------------------------------

	/// <summary>Somewhere to build a pose. Hand it back with <see cref="Free"/>.</summary>
	public int Alloc()
	{
		var pose = CoreAnimRuntime.PoseAlloc( instance );
		if ( pose >= 0 ) borrowed.Add( pose );

		return pose;
	}

	public void Free( int pose )
	{
		if ( pose < 0 ) return;

		borrowed.Remove( pose );
		CoreAnimRuntime.PoseFree( instance, pose );
	}

	/// <summary>A pose of the model at rest, which is what nothing looks like.</summary>
	public int RestPose()
	{
		var pose = Alloc();
		if ( pose >= 0 ) CoreAnimRuntime.PoseRest( instance, pose );

		return pose;
	}

	/// <summary>Every bone at nothing - what an additive difference is measured from.</summary>
	public int ZeroPose()
	{
		var pose = Alloc();
		if ( pose >= 0 ) CoreAnimRuntime.PoseZero( instance, pose );

		return pose;
	}

	public void Sample( int pose, AnimClipData clip, float time, bool looping )
		=> CoreAnimRuntime.PoseSample( instance, pose, clip, time, looping );

	public void Copy( int destination, int source )
		=> CoreAnimRuntime.PoseCopy( instance, destination, source );

	//Written into a rather than into a third pose: a chain of blends would otherwise allocate one for every link, and the first is already the caller's to lose.
	/// <summary>Blends <paramref name="b"/> into <paramref name="a"/> and hands back the one that holds the answer, freeing the other.</summary>
	public int Blend( int a, int b, float weight, int mask = -1 )
	{
		if ( a < 0 ) return b;
		if ( b < 0 ) return a;

		CoreAnimRuntime.PoseBlend( instance, a, a, b, weight, mask );
		Free( b );

		return a;
	}

	/// <summary>A difference laid on where the clip was authored: model space.</summary>
	public int AdditiveModel( int under, int over, float weight, int mask = -1 )
	{
		if ( under < 0 ) return over;
		if ( over < 0 ) return under;

		CoreAnimRuntime.PoseAdditiveModel( instance, under, under, over, weight, mask );
		Free( over );

		return under;
	}

	/// <summary>The same, for a layer that only says which way bones point.</summary>
	public int BlendRotation( int a, int b, float weight, int mask = -1 )
	{
		if ( a < 0 ) return b;
		if ( b < 0 ) return a;

		CoreAnimRuntime.PoseBlendRotation( instance, a, a, b, weight, mask );
		Free( b );

		return a;
	}

	/// <summary>The same, blended where the bones stand rather than under their parents.</summary>
	public int BlendModel( int a, int b, float weight, int mask, bool rotationOnly )
	{
		if ( a < 0 ) return b;
		if ( b < 0 ) return a;

		CoreAnimRuntime.PoseBlendModel( instance, a, a, b, weight, mask, rotationOnly );
		Free( b );

		return a;
	}

	/// <summary>The same, for a difference laid on top rather than blended.</summary>
	public int Additive( int under, int over, float weight, int mask = -1, bool separately = false )
	{
		if ( under < 0 ) return over;
		if ( over < 0 ) return under;

		CoreAnimRuntime.PoseAdditive( instance, under, under, over, weight, mask, separately );
		Free( over );

		return under;
	}

	//Make a pose the one the model is actually standing in. The bones the model drives itself, worked out on the finished pose. After everything else and
	//before the pose is handed over: a twist bone takes its share of whatever the wrist ended up doing, whether that came from a clip, a blend or a solve.
	private void Constrain( int pose )
	{
		if ( Model is null || pose < 0 ) return;

		var found = CoreSkeletons.Constraints( Model );
		if ( found.Length == 0 ) return;

		CoreAnimConstraints.Apply( instance, pose, found );
	}

	public void Apply( int pose )
	{
		if ( pose < 0 ) return;

		Constrain( pose );

		CoreAnimRuntime.PoseApply( instance, pose );
	}

	//-- bone masks -----------------------------------------------------------

	public int AllocMask()
	{
		var mask = CoreAnimRuntime.MaskAlloc( instance );
		if ( mask >= 0 ) borrowedMasks.Add( mask );

		return mask;
	}

	public void FreeMask( int mask )
	{
		if ( mask < 0 ) return;

		borrowedMasks.Remove( mask );
		CoreAnimRuntime.MaskFree( instance, mask );
	}

	public void FillMask( int mask, float weight )
		=> CoreAnimRuntime.MaskFill( instance, mask, weight );

	public void SetMaskBone( int mask, int bone, float weight, bool withChildren )
		=> CoreAnimRuntime.MaskSetBone( instance, mask, bone, weight, withChildren );

	public void BlendMask( int destination, int a, int b, float weight )
		=> CoreAnimRuntime.MaskBlend( instance, destination, a, b, weight );

	/// <summary>A bone by name, or -1.</summary>
	public int FindBone( string name ) => Skeleton?.FindBone( name ) ?? -1;

	//A safety net rather than the normal path: a node that returns early - because an input was missing, because a graph is half-written - would otherwise leak a pose a frame, and a leak measured in frames is a leak nobody catches by reading.
	/// <summary>Give back anything a frame borrowed and did not return.</summary>
	public void End()
	{
		foreach ( var pose in borrowed ) CoreAnimRuntime.PoseFree( instance, pose );
		foreach ( var mask in borrowedMasks ) CoreAnimRuntime.MaskFree( instance, mask );

		borrowed.Clear();
		borrowedMasks.Clear();
	}
}
