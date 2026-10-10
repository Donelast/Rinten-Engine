using Microsoft.AspNetCore.Components;
using Rinten.Internal;
using Rinten.Rendering;
using System.Threading;

namespace Rinten.UI;

/// <summary>A simple User Interface panel. Can be styled with <a href="https://en.wikipedia.org/wiki/CSS">CSS</a>.</summary>
[Library( "panel" ), Alias( "div", "span" ), Expose]
[Title( "Panel" ), Icon( "view_quilt" )]
public partial class Panel : IPanel, IValid, IComponent
{
	/// <summary>The element name. If you've created this Panel via a template this will be whatever the element name is on there. If not then it'll be the name of the class (ie Panel, Button)</summary>
	[Property]
	public string ElementName { get; set; }

	/// <summary>Works the same as the html id="" attribute. If you set Id to "poop", it'll match any styles that define #poop in their selector.</summary>
	public string Id
	{
		get;
		set;
	}

	//For a viewport that is a panel of a window drawn by the engine's own renderer. The panel draws no background of its own while this is set; whoever owns the window says what is drawn there - see CoreUiWindow.
	/// <summary>Whether the world is drawn where this panel is, in its place in the paint order: what is behind it is drawn first, then the world over the panel's box, then whatever is on top of it.</summary>
	[Hide]
	public bool WearsWorld { get; set; }

	/// <summary>If this was created by razor, this is the file in which it was created</summary>
	[Hide]
	public string SourceFile { get; set; }

	/// <summary>If this was created by razor, this is the line number in the file</summary>
	[Hide]
	public int SourceLine { get; set; }

	/// <summary>If this was created by razor, the column the tag starts at on that line, counted from 0 - or -1 when the line is only near: a tag inside a run of static markup, whose exact place the compiler did not keep. Tells two tags on one line apart.</summary>
	[Hide]
	public int SourceColumn { get; set; }

	/// <summary>Quick access to timing events, for async/await.</summary>
	public TaskSource Task = new( 1 );

	/// <summary>A collection of stylesheets applied to this panel directly.</summary>
	public StyleSheetCollection StyleSheet;


	//Start with the intro flag on
	PseudoClass _pseudoClass = PseudoClass.Intro;

	/// <summary>Special flags used by the styling system for hover, active etc..</summary>
	[Property]
	public PseudoClass PseudoClass
	{
		get => _pseudoClass;
		set
		{
			if ( _pseudoClass == value )
				return;

			var changed = _pseudoClass ^ value;
			_pseudoClass = value;

			PseudoClassChanged( changed );
		}
	}

	/// <summary>Whether this panel has the <c>:focus</c> pseudo class active.</summary>
	[Hide]
	public bool HasFocus => (PseudoClass & PseudoClass.Focus) != 0;

	/// <summary>Whether this panel has the <c>:active</c> pseudo class active.</summary>
	[Hide]
	public bool HasActive => (PseudoClass & PseudoClass.Active) != 0;

	/// <summary>Whether this panel has the <c>:hover</c> pseudo class active.</summary>
	[Hide]
	public bool HasHovered => (PseudoClass & PseudoClass.Hover) != 0;

	/// <summary>Whether this panel has the <c>:intro</c> pseudo class active.</summary>
	[Hide]
	public bool HasIntro => (PseudoClass & PseudoClass.Intro) != 0;

	/// <summary>Whether this panel has the <c>:outro</c> pseudo class active.</summary>
	[Hide]
	public bool HasOutro => (PseudoClass & PseudoClass.Outro) != 0;


	public Panel()
	{
		InitializeEvents();

		LayoutTree = new PanelLayout( this );
		Style = new PanelStyle( this );
		StyleSheet = new StyleSheetCollection( this );
		Transitions = new Transitions( this );
		LayerCommandList = new CommandList( $"UI Layer: {GetType().Name}" );

		ElementName = GetType().Name.ToLower();
		Switch( PseudoClass.Empty, true );

		ticksByType = TypeTicks( GetType() );
		hashOnlyByType = TypeHashOnly( GetType() );
		ticksEveryFrame = TicksEveryFrameOfItsOwn;
		TickDemand = ticksEveryFrame ? 1 : 0;

		LoadStyleSheet();
	}

	public Panel( Panel parent ) : this()
	{
		if ( parent != null )
			Parent = parent;
	}

	public Panel( Panel parent, string classnames ) : this( parent )
	{
		if ( classnames != null )
			AddClass( classnames );
	}

	internal virtual void AddToLists()
	{
		Rinten.Event.Register( this );
	}

	internal virtual void RemoveFromLists()
	{
		Rinten.Event.Unregister( this );
	}

	/// <summary>Called when a hotload happened. (Not necessarily on this panel)</summary>
	public virtual void OnHotloaded()
	{
		LoadStyleSheet();
		InitializeEvents();

		//If the checksum changed on our render tree, then we have to assume that everthing about it changed. Lets destroy it and start from scratch.
		if ( razorLastTreeChecksum != GetRenderTreeChecksum() )
		{
			razorLastTreeChecksum = GetRenderTreeChecksum();
			if ( renderTree != null )
			{
				renderTree?.Clear();
				razorTreeDirty = true;
			}
		}

		//Some of our children may have stopped existing. Like if they deleted the type - then we'll have nulls here. Lets prune them out.
		_children?.RemoveAll( x => x is null );
		_renderChildren?.RemoveAll( x => x is null );

		if ( _childrenHash is not null && _childrenHash.Any( x => x == null ) )
			_childrenHash = _childrenHash.Where( x => x != null ).ToHashSet();

		foreach ( var child in Children )
		{
			try
			{
				child.OnHotloaded();
			}
			catch ( System.Exception e )
			{
				Log.Error( e );
			}
		}

		//Children first, so their counts are fresh when this one is summed.
		RecountTickDemand();
		RequestTick();

		//New code may draw anything; nothing kept from before it stands.
		MoveGeneration();
	}

	//For a panel tree shown inside another one that it was not written for - a game's panel drawn on the UI editor's canvas, where the editor's own sheet would otherwise dress it in .row and .hidden . The panel's own sheets still apply, and so do the sheets under it.
	/// <summary>Whether the sheets above this panel stop here: nothing below it is styled by a sheet on an ancestor of it.</summary>
	[Hide]
	public bool StyleBoundary { get; set; }

	/// <summary>List of all <see cref="UI.StyleSheet"/>s applied to this panel and all its <see cref="AncestorsAndSelf">ancestors</see>, up to and including the nearest <see cref="StyleBoundary"/>.</summary>
	[Hide]
	public IEnumerable<StyleSheet> AllStyleSheets
	{
		get
		{
			foreach ( var p in AncestorsAndSelf )
			{
				if ( p.StyleSheet.List != null )
				{
					foreach ( var sheet in p.StyleSheet.List )
						yield return sheet;
				}

				if ( p.StyleBoundary ) yield break;
			}
		}
	}

	/// <summary>Switch a pseudo class on or off.</summary>
	public bool Switch( PseudoClass c, bool state )
	{
		if ( state == ((PseudoClass & c) != 0) ) return false;

		if ( state )
		{
			PseudoClass |= c;
		}
		else
		{
			PseudoClass &= ~c;
		}

		return true;
	}

	internal static void Switch( PseudoClass c, bool state, Panel panel, Panel unlessAncestorOf = null )
	{
		if ( panel == null )
			return;

		foreach ( var target in panel.AncestorsAndSelf )
		{
			if ( unlessAncestorOf != null && unlessAncestorOf.IsAncestor( target ) )
				continue;

			target.Switch( c, state );
		}
	}

	/// <summary>Return true if this panel isn't hidden by opacity or displaymode.</summary>
	[Hide]
	public bool IsVisible { get; internal set; } = true;

	/// <summary>Return true if this panel isn't hidden by opacity or displaymode.</summary>
	[Hide]
	public bool IsVisibleSelf { get; internal set; } = true;

	/// <summary>Called every frame. This is your "Think" function.</summary>
	public virtual void Tick()
	{

	}

	/// <summary>Called after the parent of this panel has changed.</summary>
	public virtual void OnParentChanged()
	{

	}

	/// <summary>Returns true if this panel would like the mouse cursor to be visible.</summary>
	public virtual bool WantsMouseInput()
	{
		if ( ComputedStyle == null )
			return false;

		if ( !IsVisibleSelf )
			return false;

		if ( ComputedStyle.PointerEvents == PointerEvents.All )
			return true;

		if ( _children is null )
			return false;

		foreach ( var child in _children )
		{
			if ( child?.WantsMouseInput() ?? false )
				return true;
		}

		return false;
	}

	//What this panel calls itself in the -ticktypes table. The type's own name, except where the type is a shell in front of something
	//else - every widget in the editor is a WidgetSurface , and "WidgetSurface is the expensive one" says nothing about which widget.
	protected internal virtual string TickTypeName => GetType().Name;

	//Named profiler zones per tick step, only with -ticktrace or -uitrace since they're expensive
	internal static readonly bool TracingTick =
		Rinten.Utility.CommandLine.HasSwitch( "-ticktrace" ) ||
		Rinten.Utility.CommandLine.HasSwitch( "-uitrace" );

	//A zone that is only a zone under -uitrace .
	private readonly ref struct TickZone
	{
		private readonly Rinten.Diagnostics.Performance.ScopeSection section;

		public TickZone( string name )
		{
			section = TracingTick ? Rinten.Diagnostics.Performance.Scope( name ) : default;
		}

		public void Dispose() => section.Dispose();
	}

	//Per-thread stack of children copies used by TickInternal, since a tick may modify the tree
	[ThreadStatic] static Panel[] walking;
	[ThreadStatic] static int walkingTop;

	//Checks whether any child would tick before copying the list, which most frames none do
	static bool AnyChildTakesATurn( List<Panel> children )
	{
		for ( var i = 0; i < children.Count; i++ )
		{
			var child = children[i];
			if ( child is null ) continue;

			if ( child.TickDemand != 0 || child.tickRequested || child.subtreeRequested ) return true;
		}

		return false;
	}

	internal void TickInternal()
	{
		if ( Application.IsHeadless )
			return;

		//The root says for the whole walk below it whether this is the editor's tree - each time it enters,
		//never from whoever called it. A panel ticked on its own, with no root above it, is taken for a game's.
		if ( this is RootPanel root )
			tickingEditorUi = root.IsEditorUi;
		else if ( Parent is null )
			tickingEditorUi = false;

		//Panels with nothing to do are skipped in game UI too, based on tick demand and dirty flags
		var skip = TickSkip;

		//Whether this panel has anything of its own to do this frame: work of its kind every frame, something asked for since the last
		//one, or a flag the body consumes still standing. Otherwise only the children that do are visited. See Panel.TickDemand.cs.
		var wanted = !skip || ticksEveryFrame || tickRequested
			|| templateBindsChanged || razorTreeDirty || ParentHasChanged
			|| ( Style?.IsDirty ?? false ) || KeepsTicking;

		//Under -ticktypes: which of the reasons above let this panel in, and the time its own turn took, children excluded. See Panel.TickReasons.cs.
		var counting = TickTypes.Counting;
		var reason = counting && wanted ? WhyWanted( skip ) : default;
		long selfTicks = 0;
		long selfFrom = counting ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

		if ( counting ) System.Threading.Interlocked.Increment( ref TickReasons.Visited );

		//The hash as the gate. A panel here only because its type answers BuildHash, with nothing asked of it and nothing
		//of its own moving, asks the hash and nothing else - and takes its turn only if the answer moved. See TickLean.
		if ( wanted && skip && Lean && !tickRequested && !templateBindsChanged && !razorTreeDirty
			&& !ParentHasChanged && !treeHasBinds && classBinds is null
			&& !( Style?.IsDirty ?? false ) && !KeepsTicking && ( TickWhenAsked || hashOnlyByType ) )
		{
			InternalBuildHash();

			if ( !razorTreeDirty )
			{
				wanted = false;
				if ( counting ) reason = TickReasons.Reason.HashUnchanged;
			}
		}

		tickRequested = false;
		subtreeRequested = false;

		if ( IsDeleting )
		{
			//we're probably transitioning out so make sure we keep updating the layout
			SetNeedsPreLayout();
			RequestTick();
			return;
		}

		try
		{
			var didBuildRenderTree = false;
			var isFirstRender = renderTree == null;

			if ( wanted )
			{
				using ( new TickZone( "tick index" ) )
				{
					UpdateSceneIndex();
				}

				if ( ParentHasChanged )
				{
					ParentHasChanged = false;
					OnParentChanged();

					//Our ancestor stylesheets have changed, so our candidate rules need rebuilding
					Style?.InvalidateBroadphase();
					StyleSelectorsChanged( true, true );
				}

				//Every panel is asked whether its draw inputs moved; override BuildHash rather than check in Tick
				using ( new TickZone( "tick buildhash" ) )
				{
					InternalBuildHash();
				}

				if ( HasRenderTree || templateBindsChanged )
				{
					//Editor windows poll binds every few frames; games, changed parameters and rebuilds poll now
					var governed = tickingEditorUi && BindPollInterval > 1 && !templateBindsChanged && !razorTreeDirty;

					if ( !governed || ( ++bindPollPhase % BindPollInterval ) == 0 )
					{
						if ( governed ) TreeBindPolls++;

						using ( new TickZone( "tick treebinds" ) )
						{
							InternalTreeBinds();
						}
					}
					else
					{
						TreeBindDeferrals++;
					}

					if ( templateBindsChanged )
					{
						templateBindsChanged = false;
						razorTreeDirty = true;
						ParametersChanged( true );
					}

					if ( razorTreeDirty )
					{
						using var razorZone = new TickZone( "tick razor" );
						InternalRenderTree();
						didBuildRenderTree = true;
					}
				}

				//A panel without a render tree has nothing to rebuild, so the flag is consumed here. It starts true and only
				//a tree ever cleared it, which let every plain panel take a full turn whenever the walk passed through.
				if ( Lean && razorTreeDirty && !HasRenderTree )
					razorTreeDirty = false;

				//keep before and after updated
				using ( new TickZone( "tick pseudo" ) )
				{
					UpdateBeforeAfterElements();
					UpdateScrollbars();
				}
				//If our style is dirty, or we're animating/transitioning/scrolling, make sure we get laid out
				var dirtyWhy = Style is null ? null
					: Style.IsDirty ? "dirty: style"
					: HasActiveTransitions ? "dirty: transition"
					: (ComputedStyle?.IsAnimationActive ?? false) ? "dirty: animation"
					: (ScrollVelocity != 0 || isScrolling || IsDragScrolling) ? "dirty: scroll"
					: null;

				if ( dirtyWhy is not null )
				{
					using ( new TickZone( dirtyWhy ) )
					{
						SetNeedsPreLayout();
					}
				}
			}

			//Don't tick our children if we're not visible. Walk a copy since ticks may change the tree
			if ( counting ) selfTicks += System.Diagnostics.Stopwatch.GetTimestamp() - selfFrom;

			if ( IsVisible && _children is { Count: > 0 } children && ( !skip || AnyChildTakesATurn( children ) ) )
			{
				using var zone = new TickZone( "tick children" );

				//The copy goes on the thread's walk stack - see walking - above whatever the ancestors put there. Read
				//through the field every time: a child deeper down may grow the array while this level is still being walked.
				var count = children.Count;
				var from = walkingTop;

				walking ??= new Panel[256];
				if ( walking.Length < from + count ) System.Array.Resize( ref walking, Math.Max( walking.Length * 2, from + count ) );

				children.CopyTo( walking, from );
				walkingTop = from + count;

				try
				{
					for ( int i = count - 1; i >= 0; i-- )
					{
						var child = walking[from + i];

						if ( child is null ) continue;
						if ( child.Parent != this ) continue;

						//A subtree with nothing in it that ticks every frame, and nothing asked for, is left alone.
						if ( skip && child.TickDemand == 0 && !child.tickRequested && !child.subtreeRequested ) continue;

						child.TickInternal();
					}
				}
				finally
				{
					System.Array.Clear( walking, from, count );
					walkingTop = from;
				}
			}

			if ( !wanted )
			{
				//A panel that only asked its hash is counted, with that cost.
				if ( counting && reason == TickReasons.Reason.HashUnchanged )
					TickReasons.Add( reason, selfTicks );

				return;
			}

			if ( counting ) selfFrom = System.Diagnostics.Stopwatch.GetTimestamp();

			//Defer OnAfterTreeRender so that children are all processed too
			if ( didBuildRenderTree )
			{
				OnAfterTreeRender( isFirstRender );
			}

			using ( new TickZone( "tick events" ) )
			{
				RunPendingEvents();
			}

			//Named by type while tracing, because "tick self" aggregated across every panel says how much the editor
			//spends in Tick and nothing about which panel spends it. The string is only built when the switch is on.
			using ( new TickZone( TracingTick ? $"tick self: {GetType().Name}" : "tick self" ) )
			{
				if ( TickTypes.Counting )
				{
					var began = System.Diagnostics.Stopwatch.GetTimestamp();
					Tick();
					TickTypes.Add( TickTypeName, System.Diagnostics.Stopwatch.GetTimestamp() - began );
				}
				else
				{
					Tick();
				}
			}

			using ( new TickZone( "tick events" ) )
			{
				RunPendingEvents();
			}

			using ( new TickZone( "tick scroll" ) )
			{
				AddScrollVelocity();
			}

			using ( new TickZone( "tick binds" ) )
			{
				RunClassBinds();
			}

			//Something of this panel's own is still moving, so the next frame has to come back to it.
			if ( KeepsTicking )
			{
				if ( counting )
					TickReasons.NoteMotion( TickTypeName, HasActiveTransitions, ComputedStyle?.IsAnimationActive ?? false,
						ScrollVelocity != 0 || isScrolling || IsDragScrolling, PendingEvents is { Count: > 0 } );

				RequestTick();
			}

			if ( counting )
			{
				var spent = selfTicks + System.Diagnostics.Stopwatch.GetTimestamp() - selfFrom;
				TickReasons.Add( reason, spent );

				//Who asks, by type - the reason alone says a third of a millisecond goes to panels that asked, and nothing about which ones keep asking.
				if ( reason == TickReasons.Reason.Requested ) TickReasons.AddAsker( TickTypeName, spent );
			}
		}
		catch ( System.Exception e )
		{
			Log.Error( e );
		}

	}

	//The first of the reasons in TickInternal that let this panel in. Diagnostic only.
	TickReasons.Reason WhyWanted( bool skip )
	{
		if ( !skip ) return TickReasons.Reason.GameInterface;
		if ( tickRequested ) return TickReasons.Reason.Requested;
		if ( razorTreeDirty ) return TickReasons.Reason.RazorDirty;
		if ( templateBindsChanged ) return TickReasons.Reason.BindParameters;
		if ( ParentHasChanged ) return TickReasons.Reason.ParentChanged;
		if ( Style?.IsDirty ?? false ) return TickReasons.Reason.StyleDirty;
		if ( KeepsTicking ) return TickReasons.Reason.InMotion;

		var type = GetType();

		if ( Overrides( type, nameof( Tick ) ) ) return TickReasons.Reason.EveryFrameTick;
		if ( Overrides( type, nameof( BuildHash ) ) ) return TickReasons.Reason.EveryFrameBuildHash;
		if ( classBinds is not null && !ticksByType && ChildContent is null ) return TickReasons.Reason.EveryFrameClassBinds;

		return TickReasons.Reason.EveryFrameRazorTree;
	}

	internal int GetRenderOrderIndex()
	{
		return (SiblingIndex) + (ComputedStyle?.ZIndex ?? 0);
	}

	/// <summary>Convert a point from the screen to a point representing a delta on this panel where the top left is [0,0] and the bottom right is [1,1]</summary>
	public Vector2 ScreenPositionToPanelDelta( Vector2 pos )
	{
		pos = ScreenPositionToPanelPosition( pos );

		var x = pos.x.LerpInverse( 0, Box.Rect.Width, false );
		var y = pos.y.LerpInverse( 0, Box.Rect.Height, false );

		return new Vector2( x, y );
	}

	/// <summary>Convert a point from the screen to a position relative to the top left of this panel</summary>
	public Vector2 ScreenPositionToPanelPosition( Vector2 pos )
	{
		//Through every transform above this panel and its own - see ToLaidOutSpace.
		pos = ToLaidOutSpace( pos );

		var x = pos.x - Box.Rect.Left;
		var y = pos.y - Box.Rect.Top;

		return new Vector2( x, y );
	}

	/// <summary>Convert a point from local space to screen space</summary>
	public Vector2 PanelPositionToScreenPosition( Vector2 pos )
	{
		var screenPos = new Vector2( pos.x + Box.Rect.Left, pos.y + Box.Rect.Top );

		return ToDrawnSpace( screenPos );
	}

	/// <summary>Screen-space rect in style coordinates for anchored overlays.</summary>
	public Rect GetScreenStyleRect()
	{
		var pixel = GetScreenPixelRect();
		var scale = ScaleFromScreen;

		return new Rect(
			pixel.Left * scale,
			pixel.Top * scale,
			pixel.Width * scale,
			pixel.Height * scale );
	}

	/// <summary>Screen-space rect in window pixels.</summary>
	public Rect GetScreenPixelRect()
	{
		var topLeft = PanelPositionToScreenPosition( Vector2.Zero );
		var bottomRight = PanelPositionToScreenPosition( Box.Rect.Size );

		return new Rect(
			topLeft.x,
			topLeft.y,
			bottomRight.x - topLeft.x,
			bottomRight.y - topLeft.y );
	}

	/// <summary>Find and return any children of this panel (including self) within the given rect.</summary>
	public IEnumerable<Panel> FindInRect( Rect box, bool fullyInside )
	{
		if ( !IsVisible )
			yield break;

		if ( !IsInside( box, fullyInside ) )
			yield break;

		yield return this;

		if ( !HasChildren )
			yield break;

		foreach ( var child in Children )
		{
			foreach ( var found in child.FindInRect( box, fullyInside ) )
			{
				yield return found;
			}
		}
	}

	/// <summary>Allow selecting child text</summary>
	[Category( "Selection" )]
	public bool AllowChildSelection { get; set; }

	[Hide]
	public bool IsValid => LayoutTree is not null;

	string CollectSelectedChildrenText( Panel p )
	{
		if ( !p.IsVisible )
			return null;

		if ( p is Rinten.UI.Label label )
		{
			return label.GetSelectedText();
		}

		string selection = null;

		var lines = p.ComputedStyle.FlexDirection == FlexDirection.Column;

		foreach ( var child in p.Children )
		{
			var sel = CollectSelectedChildrenText( child );
			if ( string.IsNullOrEmpty( sel ) ) continue;

			if ( selection == null ) selection = sel;
			else selection = $"{selection}{(lines ? "\n" : " ")}{sel}";
		}

		return selection;
	}

	//Called when the player moves the mouse after "press and holding" (or dragging) the panel.
	protected virtual void OnDragSelect( SelectionEvent e )
	{
		if ( AllowChildSelection )
		{
			e.StopPropagation();

			foreach ( var child in Children )
			{
				UpdateSelection( child, e );
			}
		}
	}

	/// <summary>If AllowChildSelection is enabled, we'll try to select all children text</summary>
	public void SelectAllInChildren()
	{
		if ( this is Rinten.UI.Label label )
		{
			label.ShouldDrawSelection = true;
			label.SelectionStart = 0;
			label.SelectionEnd = int.MaxValue;
			return;
		}

		foreach ( var child in Children )
		{
			child.SelectAllInChildren();
		}
	}

	/// <summary>Clear any selection in children</summary>
	public void UnselectAllInChildren()
	{
		if ( this is Rinten.UI.Label label )
		{
			label.ShouldDrawSelection = false;
			return;
		}

		foreach ( var child in Children )
		{
			child.UnselectAllInChildren();
		}
	}

	void UpdateSelection( Panel p, SelectionEvent e )
	{
		var rect = e.SelectionRect;

		//child is outside of selection vertically
		if ( p.Box.Rect.Bottom < rect.Top || p.Box.Rect.Top > rect.Bottom )
		{
			p.UnselectAllInChildren();
			return;
		}

		//Selectable
		if ( p is Rinten.UI.Label label )
		{
			label.ShouldDrawSelection = true;

			if ( e.StartPoint.y > e.EndPoint.y )
			{
				(e.EndPoint, e.StartPoint) = (e.StartPoint, e.EndPoint);
			}

			var start = p.Box.Rect.Top < rect.Top;
			var end = p.Box.Rect.Bottom > e.EndPoint.y;
			var negwidth = (e.EndPoint - e.StartPoint).x < 0;

			//selectAll
			if ( start && end )
			{
				label.SelectionStart = label.GetLetterAtScreenPosition( new Vector2( rect.Left, rect.Top ) );
				label.SelectionEnd = label.GetLetterAtScreenPosition( new Vector2( rect.Right, rect.Bottom ) );
			}
			else if ( start )
			{
				var from = negwidth ? rect.Right : rect.Left;
				label.SelectionStart = label.GetLetterAtScreenPosition( new Vector2( from, rect.Top ) );
				label.SelectionEnd = int.MaxValue;
			}
			else if ( end )
			{
				var to = negwidth ? rect.Left : rect.Right;
				label.SelectionStart = 0;
				label.SelectionEnd = label.GetLetterAtScreenPosition( new Vector2( to, rect.Bottom ) );
			}
			else
			{
				label.SelectionStart = 0;
				label.SelectionEnd = int.MaxValue;
			}

			return;
		}

		foreach ( var child in p.Children )
		{
			UpdateSelection( child, e );
		}
	}

	/// <summary>Called when the current language has changed. This allows you to rebuild anything that might need rebuilding. Tokenized text labels should automatically update.</summary>
	public virtual void LanguageChanged()
	{
		foreach ( var child in Children )
		{
			child.LanguageChanged();
		}
	}

	/// <summary>Invoke a method after a delay. If the panel is deleted before this delay the method will not be called.</summary>
	public async void Invoke( float seconds, Action action )
	{
		await Task.DelaySeconds( seconds );
		if ( !this.IsValid() ) return;

		action.InvokeWithWarning();
	}

	Dictionary<string, CancellationTokenSource> invokes = new();

	/// <summary>Invoke a method after a delay. If the panel is deleted before this delay the method will not be called. If the invoke is called while the old one is waiting, the old one will be cancelled.</summary>
	public async void InvokeOnce( string name, float seconds, Action action )
	{
		CancelInvoke( name );

		var tokenSource = new CancellationTokenSource();
		invokes[name] = tokenSource;

		await Task.DelaySeconds( seconds );
		if ( !this.IsValid() ) return;

		if ( tokenSource.IsCancellationRequested )
			return;

		invokes.Remove( name );
		action.InvokeWithWarning();
	}

	/// <summary>Cancel a named invocation</summary>
	public void CancelInvoke( string name )
	{
		if ( invokes.Remove( name, out var cts ) )
		{
			cts.Cancel();
			cts.Dispose();
		}
	}

}
