using System.Collections.Concurrent;
using System.Reflection;

namespace Rinten.UI;

public partial class Panel
{
	/// <summary>Whether a panel with nothing to do is left out of the frame's tick, in the editor's own windows. A game's interface ticks every panel every frame regardless; <c>ui_tick_skip 0</c> does so in the editor too, as before.</summary>
	[ConVar( "ui_tick_skip", Help = "Skip the per-frame tick of the editor's own UI panels that have nothing to do; 0 ticks every panel every frame, as a game's interface always does" )]
	public static bool TickSkip { get; set; } = true;

	/// <summary>Editor tick only does what can have changed: bind polling, hash checks, explicit requests</summary>
	[ConVar( "ui_tick_lean", Help = "Tick an editor panel only for what can have changed: razor trees only if they have binds, BuildHash-only panels only when the hash moves; 0 is the old tick. Decided per type when its first panel is made - use -noticklean to compare from the start" )]
	public static bool TickLean { get; set; } = true;

	//-noticklean : the old tick from the first panel on, for an A/B.
	static readonly bool noTickLean = Rinten.Utility.CommandLine.HasSwitch( "-noticklean" );

	//Whether the lean tick is on - the convar, unless the command line asked for the old one.
	static bool Lean => TickLean && !noTickLean;

	//Whether this panel's render tree holds a :bind , which has to be polled.
	bool treeHasBinds;

	//The root whose tick is running is the editor's. Set by the root each time it enters TickInternal and read by
	//everything below it; a game's root, in a build or played inside the editor, sets it false and gets the plain walk.
	static bool tickingEditorUi;

	/// <summary>Frames between razor :bind polls in editor windows; games poll every frame</summary>
	[ConVar( "ui_bind_poll", Min = 1, Max = 60, Help = "Frames between two polls of a razor :bind in the editor's own windows; 1 polls every frame, as a game's interface always does" )]
	public static int BindPollInterval { get; set; } = 4;

	//Panels poll on different frames rather than all on the same one. Seeded per instance so a sheet built in one go does not line them up.
	static int bindPollSeed;
	int bindPollPhase = bindPollSeed++;

	//How many panels polled their binds, and how many were put off to a later frame, summed over the run; the perf report takes the difference.
	internal static long TreeBindPolls, TreeBindDeferrals;

	//How many panels in this subtree, this one included, have work of their own every frame.
	internal int TickDemand { get; private set; }

	//Whether this panel's type has work every frame: a Tick or BuildHash of its own, or a razor render tree.
	bool ticksByType;

	//TypeHashOnly for this panel's type, asked once when it is made. The walk asks it of
	//a panel every frame, and it was a dictionary lookup keyed on the type each time.
	bool hashOnlyByType;

	//The type's answer, or a render fragment or class binds given to this panel.
	bool ticksEveryFrame;

	//This panel asked to be ticked next frame - or, outside ui_tick_lean, this panel or one below it.
	bool tickRequested;

	//A descendant asked for a tick, so the walk must pass through here without ticking this panel itself
	bool subtreeRequested;

	//Whether this panel's own work has to run every frame. A type whose Tick only re-checks what already asks for a tick says no, and is ticked when asked.
	protected virtual bool TicksEveryFrameOfItsOwn => ticksByType;

	//Tick only runs when RequestTick asks, while BuildHash is still checked every frame
	protected virtual bool TickWhenAsked => false;

	private static readonly ConcurrentDictionary<System.Type, bool> tickingTypes = new();

	//Under ui_tick_lean a render tree is not work of its own every frame - see TickLean.
	private static bool TypeTicks( System.Type type ) => tickingTypes.GetOrAdd( type, static t =>
		Overrides( t, nameof( Tick ) ) || Overrides( t, nameof( BuildHash ) )
		|| ( !Lean && Overrides( t, nameof( GetRenderTreeChecksum ) ) ) );

	private static readonly ConcurrentDictionary<System.Type, bool> hashOnlyTypes = new();

	//Whether the type's per-frame work is BuildHash alone - it overrides BuildHash and not Tick.
	private static bool TypeHashOnly( System.Type type ) => hashOnlyTypes.GetOrAdd( type, static t =>
		Overrides( t, nameof( BuildHash ) ) && !Overrides( t, nameof( Tick ) ) );

	//Whether a render fragment or a bind-less tree counts as per-frame work - it does only outside ui_tick_lean.
	bool FragmentTicks => Lean ? treeHasBinds : ChildContent is not null;

	private static bool Overrides( System.Type type, string method ) =>
		type.GetMethod( method, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, System.Type.EmptyTypes, null )?.DeclaringType != typeof( Panel );

	/// <summary>Asks for a tick next frame; opted-out types must call this from setters their Tick reads</summary>
	public void RequestTick()
	{
		//Under -ticktypes: who asks, and from where. See TickReasons.NoteAsk.
		if ( TickTypes.Counting ) TickReasons.NoteAsk( this );

		if ( !Lean )
		{
			for ( var panel = this; panel is not null && !panel.tickRequested; panel = panel.Parent )
				panel.tickRequested = true;

			return;
		}

		tickRequested = true;

		//The ancestors are only a path down to here. Stops at the first one already marked:
		//the walk clears the mark top-down, so everything above a marked panel is marked too.
		for ( var panel = Parent; panel is not null && !panel.subtreeRequested; panel = panel.Parent )
			panel.subtreeRequested = true;
	}

	//Whether the frame after this one has to tick this panel again: something of its own is still in motion.
	bool KeepsTicking =>
		HasActiveTransitions
		|| ( ComputedStyle?.IsAnimationActive ?? false )
		|| ScrollVelocity != 0 || isScrolling || IsDragScrolling
		|| PendingEvents is { Count: > 0 };

	//Re-evaluates TicksEveryFrameOfItsOwn for subclasses whose answer isn't known at construction
	protected void RefreshTicksEveryFrame()
	{
		var now = TicksEveryFrameOfItsOwn || FragmentTicks || classBinds is not null;
		if ( now == ticksEveryFrame ) return;

		ticksEveryFrame = now;
		AdjustTickDemand( now ? 1 : -1 );
	}

	void AdjustTickDemand( int delta )
	{
		for ( var panel = this; panel is not null; panel = panel.Parent )
			panel.TickDemand += delta;
	}

	//Counts the subtree afresh, bottom up; after a hotload a type may tick that did not, and children may have gone.
	void RecountTickDemand()
	{
		ticksByType = TypeTicks( GetType() );
		ticksEveryFrame = TicksEveryFrameOfItsOwn || FragmentTicks || classBinds is not null;

		var demand = ticksEveryFrame ? 1 : 0;

		if ( _children is not null )
		{
			for ( int i = 0; i < _children.Count; i++ )
				demand += _children[i]?.TickDemand ?? 0;
		}

		TickDemand = demand;
	}
}
