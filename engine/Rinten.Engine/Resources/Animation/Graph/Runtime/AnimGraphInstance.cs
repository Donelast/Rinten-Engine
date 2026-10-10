using System.Text.Json.Nodes;

using Rinten.CoreAnimation;

namespace Rinten.Animation;

//Per-character running state of a shared graph; decisions are managed, sampling/blending in core
internal sealed partial class AnimGraphInstance
{
	private static readonly Logger log = new( "AnimGraph" );

	private readonly AnimGraph graph;
	private readonly GraphNode[] nodes;
	private readonly GraphNode root;

	private readonly GraphContext context;

	private readonly Dictionary<string, int> byName = new( StringComparer.OrdinalIgnoreCase );
	private readonly Value[] values;

	private readonly HashSet<string> reported = new( StringComparer.OrdinalIgnoreCase );

	//What kind of value each parameter holds, by index.
	private readonly AnimGraphBuilder.NodeKind[] kinds;

	//The names an id parameter is allowed to take, by index, so that one set by number can be told which of them was meant.
	private readonly IReadOnlyList<string>[] options;

	//Parameters reset to default once read (author's choice), so one-shot triggers don't stick
	private readonly bool[] resets;

	//Bumped when a selector changes choice; state machines receiving it return to their start state
	internal int ResetSignals { get; private set; }

	internal void SignalReset() => ResetSignals++;

	//Where each parameter starts, and what a reset puts it back to.
	private readonly Value[] defaults;

	//Which resetting parameters have been written since the last update.
	private readonly List<int> touched = new();

	//Whatever a parameter has been set to, whichever kind it is.
	private struct Value : System.IEquatable<Value>
	{
		public float Number;
		public bool Flag;
		public string Id;
		public Vector3 Vector;
		public Transform Target;
		public bool HasTarget;

		public bool Equals( Value other )
			=> Number.Equals( other.Number ) && Flag == other.Flag
			&& string.Equals( Id, other.Id, System.StringComparison.Ordinal )
			&& Vector == other.Vector && HasTarget == other.HasTarget;

		public override bool Equals( object other ) => other is Value value && Equals( value );

		public override int GetHashCode() => System.HashCode.Combine( Number, Flag, Id, Vector, HasTarget );
	}

	private readonly CoreAnimRuntime.Instance instance;
	private readonly CoreAnimRuntime.Skeleton skeleton;
	private readonly Model model;

	public AnimGraphInstance( AnimGraph graph, CoreAnimRuntime.Instance instance,
		CoreAnimRuntime.Skeleton skeleton, Model model )
	{
		this.graph = graph;
		this.instance = instance;
		this.skeleton = skeleton;
		this.model = model;

		context = new GraphContext( this, instance, skeleton, model );

		var definition = graph?.Graph;

		if ( definition is null )
		{
			nodes = Array.Empty<GraphNode>();
			values = Array.Empty<Value>();
			kinds = Array.Empty<AnimGraphBuilder.NodeKind>();
			options = Array.Empty<IReadOnlyList<string>>();
			resets = Array.Empty<bool>();
			defaults = Array.Empty<Value>();

			return;
		}

		values = new Value[definition.All.Count];
		kinds = new AnimGraphBuilder.NodeKind[definition.All.Count];
		options = new IReadOnlyList<string>[definition.All.Count];
		resets = new bool[definition.All.Count];
		defaults = new Value[definition.All.Count];

		for ( var i = 0; i < definition.All.Count; i++ )
		{
			var parameter = definition.All[i];

			byName[parameter.Name] = i;
			kinds[i] = parameter.Kind;
			options[i] = parameter.Options;
			resets[i] = parameter.AutoReset;

			defaults[i] = Starting( parameter );
			values[i] = defaults[i];
		}

		nodes = Build( definition.Nodes );

		foreach ( var node in nodes )
		{
			node.Owner = this;
			node.Link( nodes );
		}

		root = definition.Root >= 0 && definition.Root < nodes.Length ? nodes[definition.Root] : null;

		MarkShared( definition.Nodes );
	}

	//Marks nodes with several readers so they cache results; a graph is a DAG, not a tree
	private void MarkShared( IReadOnlyList<JsonObject> definition )
	{
		if ( definition is null ) return;

		var asked = new int[nodes.Length];

		for ( var i = 0; i < definition.Count && i < nodes.Length; i++ )
		{
			if ( definition[i] is not { } fields ) continue;

			//Every index this node names, once each: a node naming the same child twice is one parent, not two.
			var named = new HashSet<int>();

			Gather( fields, named );

			foreach ( var index in named )
			{
				if ( index >= 0 && index < asked.Length ) asked[index]++;
			}
		}

		for ( var i = 0; i < nodes.Length; i++ )
		{
			if ( asked[i] > 1 ) nodes[i].Shared = true;
		}
	}

	//Every whole number under a node, which is how it names another.
	private static void Gather( JsonNode node, HashSet<int> into )
	{
		switch ( node )
		{
			case JsonArray array:
				foreach ( var item in array ) Gather( item, into );
				break;

			case JsonObject fields:
				foreach ( var pair in fields )
				{
					//What a node is and what it is called name nothing.
					if ( pair.Key is "Kind" or "Name" or "Clip" ) continue;

					Gather( pair.Value, into );
				}

				break;

			case JsonValue value when value.TryGetValue<int>( out var index ):
				into.Add( index );
				break;
		}
	}

	public string Name => graph?.Name ?? "graph";

	/// <summary>Whether there is anything here to play.</summary>
	public bool IsValid => root is not null;

	//-- parameters -----------------------------------------------------------

	//Copies parameters by name from the replaced instance, so a model swap keeps what the game set
	internal void CarryOver( AnimGraphInstance was )
	{
		if ( was is null || was == this ) return;

		foreach ( var (name, index) in byName )
		{
			if ( !was.byName.TryGetValue( name, out var from ) ) continue;

			//Only what somebody actually set: a parameter still sitting at its default carries
			//nothing, and copying it would undo a default the new graph writes differently.
			if ( was.values[from].Equals( was.defaults[from] ) ) continue;

			values[index] = was.values[from];
		}
	}

	public void SetFloat( string name, float value )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		values[index].Number = value;
		values[index].Flag = value != 0.0f;

		Touched( index );
	}

	public void SetBool( string name, bool value )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		values[index].Flag = value;
		values[index].Number = value ? 1.0f : 0.0f;

		Touched( index );
	}

	/// <summary>A parameter set by number, whatever kind it turns out to be.</summary>
	public void SetInt( string name, int value )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		if ( kinds[index] == AnimGraphBuilder.NodeKind.Id )
		{
			var list = options[index];

			values[index].Id = list is not null && value >= 0 && value < list.Count ? list[value] : "";
			values[index].Number = value;

			Touched( index );

			return;
		}

		SetFloat( name, value );
	}

	/// <summary>The same read back: an id as where its name sits in the list, anything else as its number.</summary>
	public int GetInt( string name )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return 0;

		if ( kinds[index] != AnimGraphBuilder.NodeKind.Id )
			return (int)values[index].Number;

		var list = options[index];
		if ( list is null ) return 0;

		for ( var i = 0; i < list.Count; i++ )
		{
			if ( string.Equals( list[i], values[index].Id, StringComparison.OrdinalIgnoreCase ) ) return i;
		}

		return 0;
	}

	public void SetId( string name, string value )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		values[index].Id = value ?? "";

		Touched( index );
	}

	public void SetVector( string name, Vector3 value )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		values[index].Vector = value;

		Touched( index );
	}

	public void SetTarget( string name, Transform value )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		values[index].Target = value;
		values[index].HasTarget = true;
	}

	public void ClearTarget( string name )
	{
		if ( !byName.TryGetValue( name ?? "", out var index ) ) return;

		values[index].HasTarget = false;
		boneTargets?.Remove( name ?? "" );
	}

	//Kept as a name and read every update rather than resolved once: a bone moves, and what makes a hand follow one is that it is asked again each frame.
	/// <summary>A target that follows one of the model's own bones.</summary>
	public void SetTargetBone( string name, string bone )
	{
		if ( !byName.ContainsKey( name ?? "" ) ) return;

		if ( string.IsNullOrEmpty( bone ) )
		{
			ClearTarget( name );
			return;
		}

		( boneTargets ??= new( StringComparer.OrdinalIgnoreCase ) )[name] = bone;
	}

	private Dictionary<string, string> boneTargets;

	//Put this frame's bone positions into the targets that follow them.
	private void FollowBones()
	{
		if ( boneTargets is null || boneTargets.Count == 0 ) return;

		foreach ( var (name, bone) in boneTargets )
		{
			var index = skeleton?.FindBone( bone ) ?? -1;
			if ( index < 0 ) continue;

			if ( !instance.ModelBone( index, out var trs ) ) continue;

			SetTarget( name, trs.ToTransform() );
		}
	}

	//What a parameter stands at before anything has set it. The graph's own default where it declared one. A number with
	//a range that declared none starts at the bottom of it rather than at zero, which may be outside the range entirely.
	private static Value Starting( AnimGraph.GraphParameter parameter )
	{
		var value = new Value { Id = "" };

		switch ( parameter.Kind )
		{
			case AnimGraphBuilder.NodeKind.Id:
			{
				var index = (int)parameter.Default;

				value.Id = parameter.Options is { } list && index >= 0 && index < list.Count ? list[index] : "";
				value.Number = index;

				break;
			}

			case AnimGraphBuilder.NodeKind.Bool:
				value.Flag = parameter.Default != 0.0f;
				value.Number = value.Flag ? 1.0f : 0.0f;
				break;

			case AnimGraphBuilder.NodeKind.Float:
				//Use the default even when zero; a zero default is common and meaningful
				value.Number = parameter.Default;
				value.Flag = value.Number != 0.0f;
				break;

			default:
				value.Number = parameter.Default;
				break;
		}

		return value;
	}

	public bool Has( string name ) => byName.ContainsKey( name ?? "" );

	internal float GetFloat( int index ) => Inside( index ) ? values[index].Number : 0.0f;
	internal bool GetBool( int index ) => Inside( index ) && values[index].Flag;

	//What a flag stands at now, by name. For tests and for debugging.
	internal bool Flag( string name )
		=> byName.TryGetValue( name ?? "", out var index ) && GetBool( index );
	internal string GetId( int index ) => Inside( index ) ? values[index].Id ?? "" : "";
	internal Vector3 GetVector( int index ) => Inside( index ) ? values[index].Vector : Vector3.Zero;
	internal Transform GetTarget( int index ) => Inside( index ) ? values[index].Target : Transform.Zero;
	internal bool HasTarget( int index ) => Inside( index ) && values[index].HasTarget;

	private bool Inside( int index ) => index >= 0 && index < values.Length;

	//-- playing --------------------------------------------------------------

	/// <summary>Work out this frame's pose and put the model in it.</summary>
	public void Update( float delta, Transform world = default )
	{
		if ( root is null ) return;

		//Nobody is watching a graph in a game, so one that is being traced has to be given something to report into first.
		var traced = Tracing;
		var mine = traced && watching is null;

		if ( mine ) Watch();

		var started = watching is null ? 0L : System.Diagnostics.Stopwatch.GetTimestamp();

		//Anything the command line insists on, after the game has had its say and before the graph reads a thing - see Force.
		Force();

		context.World = world;
		context.Begin( delta );

		//Whatever the graph is told to follow, read off where those bones ended up last frame - which is the only place they exist.
		FollowBones();

		watching?.Begin( context.Frame );

		try
		{
			var pose = root.Pose( context );

			recording?.Take( this, pose, delta, world );

			context.Apply( pose );
			context.Free( pose );
		}
		finally
		{
			//Whatever a node borrowed and did not hand back, in case one returned early - a leak of a pose a frame is a leak nobody finds by reading.
			context.End();
		}

		Restore();

		if ( watching is not null )
			watching.UpdateTime = (float)System.Diagnostics.Stopwatch.GetElapsedTime( started ).TotalMilliseconds;

		if ( traced ) Trace();
		if ( mine ) StopWatching();
	}

	//Note that a resetting parameter has been written, so that the update which reads it can put it back afterwards.
	private void Touched( int index )
	{
		if ( !resets[index] || touched.Contains( index ) ) return;

		touched.Add( index );
	}

	//Put every resetting parameter back to its default, now that this update has read what it was set to. One that means a state rather
	//than an event is written every frame by whoever owns it, so this costs it nothing: it is set again before the next update reads it.
	private void Restore()
	{
		if ( touched.Count == 0 ) return;

		foreach ( var index in touched ) values[index] = defaults[index];

		touched.Clear();
	}

	//-- being watched ----------------------------------------------------------

	private GraphDebug watching;
	private GraphRecording recording;

	//Made on demand and never by a game: everything it costs when nothing is watching is that this is null.
	/// <summary>What the graph is doing, once something has asked to be told.</summary>
	public GraphDebug Watch()
	{
		if ( watching is not null ) return watching;

		watching = new GraphDebug( nodes.Length );
		context.Debug = watching;

		return watching;
	}

	public GraphDebug Watching => watching;

	public void StopWatching()
	{
		watching = null;
		context.Debug = null;
	}

	/// <summary>Every update the graph has made, while it is being recorded.</summary>
	public GraphRecording Recording => recording;

	public GraphRecording Record()
	{
		recording ??= new GraphRecording();
		recording.Start();

		return recording;
	}

	public void StopRecording() => recording?.Stop();

	public void ClearRecording()
	{
		recording?.Stop();
		recording = null;
	}

	/// <summary>Put the model in a recorded update, for looking at it again.</summary>
	public bool GoTo( int update )
	{
		if ( recording is null ) return false;

		return recording.Apply( this, update );
	}

	internal GraphContext Context => context;

	internal CoreAnimRuntime.Instance Instance => instance;

	//Where the graph as a whole is, in the marks of what it plays.
	internal SyncPoint CurrentSync()
	{
		if ( root is not null && root.CurrentSync( context, out var point ) ) return point;

		return new SyncPoint( "", -1, 0.0f );
	}

	/// <summary>Every parameter back to what the graph says it starts at.</summary>
	public void ResetParameters()
	{
		Array.Copy( defaults, values, values.Length );
		touched.Clear();
	}

	/// <summary>One parameter back to what the graph says it starts at. Nothing, for a name the graph has not got.</summary>
	public void ResetParameter( string name )
	{
		if ( byName.TryGetValue( name ?? "", out var index ) ) values[index] = defaults[index];
	}

	/// <summary>Start the whole graph again.</summary>
	public void Reset()
	{
		foreach ( var node in nodes ) node.Reset( context );
	}

	//What a graph played inside another one does: the poses and the frame belong to the graph that asked, and the states and parameters belong to this one - which is why a node knows its own graph rather than reading the context for it.
	/// <summary>Work this graph's pose out inside somebody else's evaluation.</summary>
	public int PoseInto( GraphContext context ) => root?.Pose( context ) ?? context.RestPose();

	//-- what the game puts in ------------------------------------------------

	private readonly Dictionary<string, int> externalPoses = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, AnimGraphInstance> externalGraphs = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, AnimGraphInstance> referenced = new( StringComparer.OrdinalIgnoreCase );

	//Kept between frames rather than borrowed for one, because the game sets it when it changes and expects it to stay. Freed when it is cleared or when the graph goes.
	/// <summary>A pose the game holds in a slot - a ragdoll, a recorded pose, anything the graph does not make itself.</summary>
	public void SetExternalPose( string slot, IReadOnlyList<Transform> bones )
	{
		if ( string.IsNullOrWhiteSpace( slot ) || bones is null || bones.Count == 0 ) return;

		if ( !externalPoses.TryGetValue( slot, out var pose ) )
		{
			pose = CoreAnimRuntime.PoseAlloc( instance );
			if ( pose < 0 ) return;

			externalPoses[slot] = pose;
		}

		Write( pose, bones );
	}

	private void Write( int pose, IReadOnlyList<Transform> bones )
	{
		var target = instance.Pose( pose );
		if ( target is null ) return;

		var count = Math.Min( bones.Count, target.Length );

		for ( var bone = 0; bone < count; bone++ )
			target[bone] = AnimTrs.FromTransform( bones[bone] );
	}

	public void ClearExternalPose( string slot )
	{
		if ( !externalPoses.Remove( slot ?? "", out var pose ) ) return;

		CoreAnimRuntime.PoseFree( instance, pose );
	}

	internal int ExternalPose( string slot )
		=> externalPoses.TryGetValue( slot ?? "", out var pose ) ? pose : -1;

	/// <summary>A whole graph the game plugs into a slot.</summary>
	public void SetExternalGraph( string slot, AnimGraph graph )
	{
		if ( string.IsNullOrWhiteSpace( slot ) ) return;

		if ( graph is null )
		{
			externalGraphs.Remove( slot );
			return;
		}

		var plugged = new AnimGraphInstance( graph, instance, skeleton, model );
		if ( !plugged.IsValid ) return;

		plugged.Reset();
		externalGraphs[slot] = plugged;
	}

	internal AnimGraphInstance ExternalGraph( string slot )
		=> externalGraphs.TryGetValue( slot ?? "", out var graph ) ? graph : null;

	//A graph named inside this one, made once and kept. Its own instance, because a referenced graph
	//has its own states: two places referring to the same graph are not in the same part of it.
	internal AnimGraphInstance Referenced( AnimGraph graph )
	{
		if ( graph is null ) return null;

		if ( referenced.TryGetValue( graph.Name, out var made ) ) return made;

		made = new AnimGraphInstance( graph, instance, skeleton, model );
		if ( !made.IsValid ) return null;

		made.Reset();
		referenced[graph.Name] = made;

		return made;
	}

	internal void MissingGraph( string path )
	{
		if ( !reported.Add( $"graph:{path}" ) ) return;

		log.Warning( $"{Name}: no graph called '{path}'" );
	}

	//-- saying what is missing -----------------------------------------------

	//A clip the graph asks for and nothing can find. Said once per name: a missing
	//clip is asked for every frame, and a log full of one line is a log nobody reads.
	internal void MissingClip( string clip )
	{
		if ( !reported.Add( $"clip:{clip}" ) ) return;

		log.Warning( $"{Name}: no clip called '{clip}'" );
	}

	internal void MissingBone( string bone )
	{
		if ( !reported.Add( $"bone:{bone}" ) ) return;

		log.Warning( $"{Name}: the model has no bone called '{bone}'" );
	}

	//-- making the nodes -----------------------------------------------------

	private static GraphNode[] Build( IReadOnlyList<JsonObject> definitions )
	{
		var built = new GraphNode[definitions.Count];

		for ( var i = 0; i < definitions.Count; i++ )
		{
			var node = Make( definitions[i] ) ?? new UnknownNode( "" );

			//Where it sits, set here rather than passed to every constructor: anything that reports
			//on a node names it by number, because that is what the editor's debugger points at.
			node.Index = i;

			built[i] = node;
		}

		return built;
	}

	private static GraphNode Make( JsonObject node )
	{
		var kind = GraphFields.Kind( node );

		return kind switch
		{
			//values
			"Const" => new ConstFloatNode( node ),
			"ConstBool" => new ConstBoolNode( node ),
			"ConstId" => new ConstIdNode( node ),
			"ConstVector" => new ConstVectorNode( node ),
			"ConstTarget" => new ConstTargetNode( node, true ),
			"NullTarget" => new ConstTargetNode( node, false ),
			"Parameter" => new ParameterNode( node ),
			"FloatMath" => new FloatMathNode( node ),
			"AngleMath" => new AngleMathNode( node ),
			"Clamp" => new ClampNode( node ),
			"Remap" => new RemapNode( node ),
			"Curve" => new CurveNode( node ),
			"Ease" => new EaseNode( node ),
			"Spring" => new SpringNode( node ),
			"Cached" => new CachedNode( node ),
			"Compare" => new CompareNode( node ),
			"InRange" => new InRangeNode( node ),
			"Not" => new NotNode( node ),
			"And" => new LogicNode( node, true ),
			"Or" => new LogicNode( node, false ),
			"Switch" => new SwitchNode( node ),
			"IdSwitch" => new IdSwitchNode( node ),
			"IdCompare" => new IdCompareNode( node ),
			"IdSelect" => Select( node ),
			"IdToFloat" => new IdToFloatNode( node ),
			"FloatSelect" => new FloatSelectNode( node ),
			"VectorCreate" => new VectorCreateNode( node ),
			"VectorInfo" => new VectorInfoNode( node ),
			"VectorNegate" => new VectorNegateNode( node ),

			//poses
			"ReferencePose" => new ReferencePoseNode(),
			"ZeroPose" => new ZeroPoseNode(),
			"Clip" => new ClipNode( node ),
			"TimedClip" => new TimedClipNode( node ),
			"Pose" => new PoseFromClipNode( node ),
			"Blend" => new BlendPoseNode( node ),
			"Passthrough" => new PassthroughNode( node ),
			"Select" => new SelectPoseNode( node ),
			"BlendSpace" => new BlendSpaceNode( node ),
			"BlendSpace2D" => new BlendSpace2DNode( node ),
			"Layers" => new LayersNode( node ),
			"SpeedScale" => new SpeedScaleNode( node ),
			"CycleControl" => new CycleControlNode( node ),
			"Subtract" => new SubtractPoseNode( node ),
			"PoseScale" => new PoseScaleNode( node ),

			//bone masks
			"BoneMask" => new BoneMaskNode( node ),
			"BoneMaskWeight" => new BoneMaskWeightNode( node ),
			"BoneMaskBlend" => new BoneMaskBlendNode( node ),
			"BoneMaskSwitch" => new BoneMaskSwitchNode( node ),
			"BoneMaskSelect" => new BoneMaskSelectNode( node ),

			//state machines
			"StateMachine" => new StateMachineNode( node ),
			"Transition" => new TransitionNode( node ),
			"StateCompleted" => new StateCompletedNode( node ),
			"TimeCondition" => new TimeConditionNode( node ),

			//what the animation is saying
			"FootstepCondition" => new FootstepConditionNode( node ),
			"FootstepPercentage" => new FootstepPercentageNode( node ),
			"FootstepEventId" => new FootstepEventIdNode( node ),
			"IdEvent" => new IdEventNode( node ),
			"IdEventCondition" => new IdEventConditionNode( node ),
			"IdEventPercentage" => new IdEventPercentageNode( node ),
			"CurveEvent" => new CurveEventNode( node ),
			"TransitionEventCondition" => new TransitionEventConditionNode( node ),
			"GraphEventCondition" => new GraphEventConditionNode( node ),
			"CurrentSyncEvent" => new CurrentSyncEventNode( node ),
			"CurrentSyncEventId" => new CurrentSyncEventIdNode( node ),
			"SyncEventCondition" => new SyncEventConditionNode( node ),

			//changing a pose after it has been made
			"Ik" => new IkNode( node ),

			//What an IK node was called when a limb had to be two bones.
			"TwoBoneIK" => new IkNode( node ),
			"LookAt" => new LookAtNode( node ),
			"FootIK" => new FootIKNode( node ),
			"RootMotionOverride" => new RootMotionOverrideNode( node ),
			"OrientationWarp" => new OrientationWarpNode( node ),
			"TargetWarp" => new TargetWarpNode( node ),
			"DurationScale" => new DurationScaleNode( node ),
			"VelocityScale" => new VelocityScaleNode( node ),
			"VelocityScaleBone" => new VelocityScaleBoneNode( node ),
			"Variation" => new VariationNode( node ),
			"HitReact" => new HitReactNode( node ),

			//somewhere to reach for
			"PointTarget" => new PointTargetNode( node ),
			"BoneTarget" => new BoneTargetNode( node ),
			"IsTargetSet" => new IsTargetSetNode( node ),
			"TargetInfo" => new TargetInfoNode( node ),
			"TargetPoint" => new TargetPointNode( node ),
			"TargetOffset" => new TargetOffsetNode( node ),
			"TargetSelect" => new TargetSelectNode( node ),

			//picking between poses by a number
			"ParameterSelect" => new ParameterSelectNode( node ),
			"VelocityBlend" => new VelocityBlendNode( node ),

			//what comes from outside the graph
			"ExternalPose" => new ExternalPoseNode( node ),
			"ExternalPoseSet" => new ExternalPoseSetNode( node ),
			"ExternalGraph" => new ExternalGraphNode( node ),
			"ExternalGraphFilled" => new ExternalGraphFilledNode( node ),
			"ReferencedGraph" => new ReferencedGraphNode( node ),

			_ => new UnknownNode( kind ),
		};
	}

	//"IdSelect" is written by two different things - a name chosen from conditions, and a pose
	//chosen by name - so which one it is has to be read off the node rather than off its kind.
	private static GraphNode Select( JsonObject node )
		=> GraphFields.Array( node, "Cases" ) is not null
			? new IdSelectValueNode( node )
			: new IdSelectPoseNode( node );
}
