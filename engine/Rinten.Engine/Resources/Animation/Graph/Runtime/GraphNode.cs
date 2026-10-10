using System.Text.Json.Nodes;

namespace Rinten.Animation;

//Running graph node; evaluated on demand (unread nodes don't run) and holds per-instance state
internal abstract class GraphNode
{
	/// <summary>Where this node sits in the definition.</summary>
	public int Index { get; internal set; }

	//Not taken from the context, because a graph can be played inside another one: a node reading a parameter means its own graph's parameter, and the context is whoever is doing the asking.
	/// <summary>The graph this node belongs to.</summary>
	public AnimGraphInstance Owner { get; internal set; }

	/// <summary>Read the node's inputs out of the definition. Called once, when the graph is loaded, with every node already made - so a node can hold its inputs directly instead of looking them up by index every frame.</summary>
	public virtual void Link( GraphNode[] nodes ) { }

	/// <summary>Start again: a graph that has just been given to something, or a state that has just been entered.</summary>
	public virtual void Reset( GraphContext context ) { }

	//-- what a node can be asked for: wrappers report to the debugger, then call the On- methods --

	//Values are cached once per frame since nodes are shared widely; poses are not (borrowed buffers)
	private int valueFrame = -1;

	private float cachedFloat;
	private bool cachedBool;
	private string cachedId;
	private Vector3 cachedVector;

	private bool haveFloat, haveBool, haveId, haveVector;

	//Whether what was worked out last is still this frame's answer.
	private bool Remembered( GraphContext context )
	{
		if ( valueFrame == context.Frame ) return true;

		valueFrame = context.Frame;
		haveFloat = haveBool = haveId = haveVector = false;

		return false;
	}

	public float Float( GraphContext context )
	{
		if ( Remembered( context ) && haveFloat ) return cachedFloat;

		var value = OnFloat( context );
		context.Debug?.Float( this, value );

		cachedFloat = value;
		haveFloat = true;

		return value;
	}

	protected virtual float OnFloat( GraphContext context ) => 0.0f;

	public bool Bool( GraphContext context )
	{
		if ( Remembered( context ) && haveBool ) return cachedBool;

		var value = OnBool( context );
		context.Debug?.Bool( this, value );

		cachedBool = value;
		haveBool = true;

		return value;
	}

	protected virtual bool OnBool( GraphContext context ) => false;

	public string Id( GraphContext context )
	{
		if ( Remembered( context ) && haveId ) return cachedId;

		var value = OnId( context );
		context.Debug?.Id( this, value );

		cachedId = value;
		haveId = true;

		return value;
	}

	protected virtual string OnId( GraphContext context ) => "";

	public Vector3 Vector( GraphContext context )
	{
		if ( Remembered( context ) && haveVector ) return cachedVector;

		var value = OnVector( context );
		context.Debug?.Vector( this, value );

		cachedVector = value;
		haveVector = true;

		return value;
	}

	protected virtual Vector3 OnVector( GraphContext context ) => Vector3.Zero;

	public Transform Target( GraphContext context )
	{
		var value = OnTarget( context );
		context.Debug?.Target( this, value, HasTarget( context ) );

		return value;
	}

	protected virtual Transform OnTarget( GraphContext context ) => Transform.Zero;

	/// <summary>Whether a target node has anything in it.</summary>
	public virtual bool HasTarget( GraphContext context ) => false;

	//Set at build when several nodes ask for this pose: computed once and copied instead of recomputed
	internal bool Shared { get; set; }

	private int poseFrame = -1;
	private int cachedPose = -1;

	public int Pose( GraphContext context )
	{
		//Somebody else has already asked for this, this frame. What they were given is theirs to blend
		//into and hand back, so this is a copy of what was worked out rather than the same buffer.
		if ( Shared && poseFrame == context.Frame && cachedPose >= 0 )
		{
			var again = context.Alloc();

			if ( again >= 0 )
			{
				context.Copy( again, cachedPose );
				return again;
			}
		}

		var pose = OnPose( context );
		context.Debug?.Pose( this, context );

		if ( Owner is { Measuring: true } ) Owner.Measured( this, pose );

		//Kept for whoever asks next. Its own copy, because what is handed back belongs to the caller -
		//and freed with everything else the frame borrowed, which is why it is only ever this frame's.
		if ( Shared && pose >= 0 )
		{
			var keep = context.Alloc();

			if ( keep >= 0 )
			{
				context.Copy( keep, pose );

				cachedPose = keep;
				poseFrame = context.Frame;
			}
		}

		return pose;
	}

	protected virtual int OnPose( GraphContext context ) => context.RestPose();

	/// <summary>A bone mask, or -1 for every bone equally.</summary>
	public int Mask( GraphContext context )
	{
		var mask = OnMask( context );
		context.Debug?.Mask( this );

		return mask;
	}

	protected virtual int OnMask( GraphContext context ) => -1;

	/// <summary>How long what this node is playing lasts, if it is the sort of thing that has a length. Zero means it does not.</summary>
	public virtual float Duration( GraphContext context ) => 0.0f;

	/// <summary>How far through that it is, from zero to one.</summary>
	public virtual float Progress( GraphContext context ) => 0.0f;

	/// <summary>Move whatever this node is playing along by a time. Called on the pose nodes that are actually reached this frame, before they are asked for a pose - so a clip nobody is playing does not advance.</summary>
	public virtual void Advance( GraphContext context, float delta ) { }

	//-- lining two things up: wrappers forward these to the clip they wrap; others do nothing --

	/// <summary>Put what this plays at a time, in seconds.</summary>
	public virtual void Seek( GraphContext context, float time ) { }

	/// <summary>The sync event the playhead is inside now, if it is inside one.</summary>
	public virtual bool CurrentSync( GraphContext context, out SyncPoint point )
	{
		point = default;
		return false;
	}

	//Two walks of different lengths are in step when the same foot is down, not when the same number of seconds have passed. Matching by mark is how a transition between them does not put both feet down at once.
	/// <summary>Move to where another animation is, by what the two have marked rather than by the clock.</summary>
	public virtual bool SeekToSync( GraphContext context, in SyncPoint point,
		AnimGraphBuilder.TransitionSyncMatch match, bool preferClosestId ) => false;

	//Only ever asked for by something watching the graph, which is why it fills in a buffer it is handed rather than making one: it is asked once a frame for every node an editor is drawing.
	/// <summary>Every mark on what this node plays, as fractions of its length.</summary>
	public virtual int SyncTrack( GraphContext context, ref AnimGraphSyncTrackEvent[] into ) => 0;

	//The animation's own speed, measured off its root motion - what a blend between a walk and a run is spread along when nobody wrote the numbers down. Zero for anything that does not travel.
	/// <summary>How fast what this node plays carries the character, on average.</summary>
	public virtual float Speed( GraphContext context ) => 0.0f;
}

//Where an animation is, said in the marks the artist put on it.
internal readonly record struct SyncPoint( string Id, int Index, float Fraction );

//A node kind the runtime does not know. Kept as a node rather than refused at load, and loud exactly once: a graph using one node
//we have not written yet should play everything else rather than not load at all, and the first frame should say which node it was.
internal sealed class UnknownNode : GraphNode
{
	private static readonly Logger log = new( "AnimGraph" );

	private readonly string kind;
	private bool said;

	public UnknownNode( string kind )
	{
		this.kind = kind ?? "";
	}

	private void SayOnce( GraphContext context )
	{
		if ( said ) return;

		said = true;
		log.Warning( $"{Owner?.Name}: node {Index} is a '{kind}', which this runtime does not do yet" );
	}

	protected override float OnFloat( GraphContext context )
	{
		SayOnce( context );
		return 0.0f;
	}

	protected override bool OnBool( GraphContext context )
	{
		SayOnce( context );
		return false;
	}

	protected override string OnId( GraphContext context )
	{
		SayOnce( context );
		return "";
	}

	protected override int OnPose( GraphContext context )
	{
		SayOnce( context );
		return context.RestPose();
	}

	protected override int OnMask( GraphContext context )
	{
		SayOnce( context );
		return -1;
	}
}

//Reading a node's fields out of the definition it was compiled into.
internal static class GraphFields
{
	public static string Kind( JsonObject node ) => Text( node, "Kind" );

	public static string Text( JsonObject node, string name, string fallback = "" )
		=> node?[name] is { } value && value.GetValueKind() == System.Text.Json.JsonValueKind.String
			? value.GetValue<string>()
			: fallback;

	public static float Number( JsonObject node, string name, float fallback = 0.0f )
		=> node?[name] is { } value && value.GetValueKind() == System.Text.Json.JsonValueKind.Number
			? value.GetValue<float>()
			: fallback;

	public static int Integer( JsonObject node, string name, int fallback = 0 )
		=> (int)Number( node, name, fallback );

	public static bool Flag( JsonObject node, string name, bool fallback = false )
	{
		if ( node?[name] is not { } value ) return fallback;

		return value.GetValueKind() switch
		{
			System.Text.Json.JsonValueKind.True => true,
			System.Text.Json.JsonValueKind.False => false,
			_ => fallback,
		};
	}

	/// <summary>A reference to another node, as the index the definition stores. Below zero means nothing was connected.</summary>
	public static int Reference( JsonObject node, string name ) => Integer( node, name, -1 );

	public static JsonArray Array( JsonObject node, string name ) => node?[name] as JsonArray;

	public static T Enum<T>( JsonObject node, string name, T fallback ) where T : struct
		=> System.Enum.TryParse<T>( Text( node, name ), out var parsed ) ? parsed : fallback;
}
