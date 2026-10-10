namespace Rinten.Animation;

//Editor-only recorder of what a running graph does; nodes report in, a null check when unwatched
internal sealed class GraphDebug
{
	private readonly Node[] nodes;

	//What is known about one node, and when it was last known.
	private struct Node
	{
		public AnimGraphNodeDebugInfo Info;

		public AnimGraphNodeDebugValue Value;
		public bool HasValue;

		public string Id;
		public string TargetBone;

		public AnimGraphSyncTrackEvent[] Sync;
		public int SyncCount;

		/// <summary>The frame this node was last asked for anything.</summary>
		public int Frame;

		//What only some kinds of node have to say.
		public int BlendFirst, BlendSecond;
		public float BlendWeight;
		public bool HasBlend;

		public Vector2 Blend2D;
		public bool HasBlend2D;

		public float[] Layers;
		public int LayerCount;

		public int State, StateFrom, StateTransition;
		public float StateProgress;
		public bool HasState;
	}

	public GraphDebug( int count )
	{
		nodes = new Node[Math.Max( count, 0 )];
	}

	private int frame = -1;

	/// <summary>How long the last update took, in milliseconds.</summary>
	public float UpdateTime { get; set; }

	/// <summary>A new frame. Nothing is cleared: a node that is not reached keeps what it last said, and is told apart from one that is by the frame it said it on.</summary>
	public void Begin( int at ) => frame = at;

	private ref Node At( GraphNode node )
	{
		var index = node.Index;

		if ( index < 0 || index >= nodes.Length ) index = 0;

		ref var entry = ref nodes[index];

		entry.Frame = frame;
		entry.Info.IsInitialized = true;
		entry.Info.IsValid = true;

		return ref entry;
	}

	//-- what the nodes say ---------------------------------------------------

	public void Float( GraphNode node, float value )
	{
		ref var entry = ref At( node );

		entry.Info.ValueType = AnimGraphNodeValueType.Float;
		entry.Value.Float = value;
		entry.HasValue = true;
	}

	public void Bool( GraphNode node, bool value )
	{
		ref var entry = ref At( node );

		//A float node asked whether it is non-zero is still a float node: the kind is only overwritten by something that has no other reading.
		if ( entry.Info.ValueType == AnimGraphNodeValueType.Unknown )
			entry.Info.ValueType = AnimGraphNodeValueType.Bool;

		entry.Value.Bool = value;
		entry.HasValue = true;
	}

	public void Id( GraphNode node, string value )
	{
		ref var entry = ref At( node );

		entry.Info.ValueType = AnimGraphNodeValueType.ID;
		entry.Value.ID = value ?? "";
		entry.Id = value ?? "";
		entry.HasValue = true;

		Remember( value );
	}

	public void Vector( GraphNode node, Vector3 value )
	{
		ref var entry = ref At( node );

		entry.Info.ValueType = AnimGraphNodeValueType.Vector;
		entry.Value.Vector = value;
		entry.HasValue = true;
	}

	public void Target( GraphNode node, Transform value, bool set )
	{
		ref var entry = ref At( node );

		entry.Info.ValueType = AnimGraphNodeValueType.Target;
		entry.Value.TargetTransform = value;
		entry.Value.IsTargetSet = set;
		entry.HasValue = true;
	}

	public void Mask( GraphNode node )
	{
		ref var entry = ref At( node );

		entry.Info.ValueType = AnimGraphNodeValueType.BoneMask;
	}

	/// <summary>A pose, which is asked about far more than it is read: where it is, how long it lasts, and what is marked on it.</summary>
	public void Pose( GraphNode node, GraphContext context )
	{
		ref var entry = ref At( node );

		entry.Info.ValueType = AnimGraphNodeValueType.Pose;
		entry.Info.Duration = node.Duration( context );
		entry.Info.CurrentTime = node.Progress( context );
		entry.Info.DeltaTime = context.Delta;

		entry.SyncCount = node.SyncTrack( context, ref entry.Sync );
		entry.Info.SyncEventCount = entry.SyncCount;

		if ( node.CurrentSync( context, out var point ) )
		{
			entry.Info.CurrentSyncEvent = point.Index;
			entry.Info.CurrentSyncEventPercentage = point.Fraction;
		}
		else
		{
			entry.Info.CurrentSyncEvent = -1;
			entry.Info.CurrentSyncEventPercentage = 0.0f;
		}
	}

	public void Blend( GraphNode node, int first, int second, float weight )
	{
		ref var entry = ref At( node );

		entry.HasBlend = true;
		entry.BlendFirst = first;
		entry.BlendSecond = second;
		entry.BlendWeight = weight;
	}

	public void Blend2D( GraphNode node, Vector2 parameter )
	{
		ref var entry = ref At( node );

		entry.HasBlend2D = true;
		entry.Blend2D = parameter;
	}

	public void Layer( GraphNode node, int layer, float weight )
	{
		if ( layer < 0 ) return;

		ref var entry = ref At( node );

		if ( entry.Layers is null || entry.Layers.Length <= layer )
		{
			var grown = new float[layer + 1];

			entry.Layers?.CopyTo( grown, 0 );
			entry.Layers = grown;
		}

		entry.Layers[layer] = weight;
		entry.LayerCount = Math.Max( entry.LayerCount, layer + 1 );
	}

	public void Machine( GraphNode node, int state, int from, int transition, float progress )
	{
		ref var entry = ref At( node );

		entry.HasState = true;
		entry.State = state;
		entry.StateFrom = from;
		entry.StateTransition = transition;
		entry.StateProgress = progress;
	}

	/// <summary>A node that could not do what it was for - a clip it cannot find.</summary>
	public void Invalid( GraphNode node )
	{
		ref var entry = ref At( node );

		entry.Info.IsValid = false;
	}

	//Ids are hashed everywhere they are used, and an editor drawing a graph wants the word the artist typed. Every id a running graph touches goes past here, which is a smaller set than every string in the game.
	/// <summary>Keep what a name was, so a token can be turned back into one.</summary>
	public void Remember( string value )
	{
		if ( string.IsNullOrEmpty( value ) ) return;

		names[new StringToken( value ).Value] = value;
	}

	private readonly Dictionary<uint, string> names = new();

	public string Name( StringToken token ) => names.TryGetValue( token.Value, out var name ) ? name : null;

	//-- what the editor reads ------------------------------------------------

	private bool Inside( int node ) => node >= 0 && node < nodes.Length;

	public bool TryGetInfo( int node, out AnimGraphNodeDebugInfo info )
	{
		info = default;

		if ( !Inside( node ) ) return false;

		info = nodes[node].Info;

		//Reached this frame, rather than reached at some point: which part of a graph is playing is the thing being drawn.
		info.IsActive = nodes[node].Frame == frame && nodes[node].Info.IsInitialized;

		return nodes[node].Info.IsInitialized;
	}

	public bool TryGetValue( int node, out AnimGraphNodeDebugValue value )
	{
		value = default;

		if ( !Inside( node ) || !nodes[node].HasValue ) return false;

		value = nodes[node].Value;

		return true;
	}

	public bool TryGetSyncEvent( int node, int index, out AnimGraphSyncTrackEvent marked )
	{
		marked = default;

		if ( !Inside( node ) ) return false;

		var track = nodes[node].Sync;
		if ( track is null || index < 0 || index >= nodes[node].SyncCount ) return false;

		marked = track[index];

		return true;
	}

	public bool TryGetBlend( int node, out int first, out int second, out float weight )
	{
		first = -1;
		second = -1;
		weight = 0.0f;

		if ( !Inside( node ) || !nodes[node].HasBlend ) return false;

		first = nodes[node].BlendFirst;
		second = nodes[node].BlendSecond;
		weight = nodes[node].BlendWeight;

		return true;
	}

	public bool TryGetBlend2D( int node, out Vector2 parameter )
	{
		parameter = default;

		if ( !Inside( node ) || !nodes[node].HasBlend2D ) return false;

		parameter = nodes[node].Blend2D;

		return true;
	}

	public bool TryGetLayerWeight( int node, int layer, out float weight )
	{
		weight = 0.0f;

		if ( !Inside( node ) ) return false;

		var layers = nodes[node].Layers;
		if ( layers is null || layer < 0 || layer >= nodes[node].LayerCount ) return false;

		weight = layers[layer];

		return true;
	}

	public bool TryGetState( int node, out int state, out int from, out int transition, out float progress )
	{
		state = -1;
		from = -1;
		transition = -1;
		progress = 0.0f;

		if ( !Inside( node ) || !nodes[node].HasState ) return false;

		state = nodes[node].State;
		from = nodes[node].StateFrom;
		transition = nodes[node].StateTransition;
		progress = nodes[node].StateProgress;

		return true;
	}
}
