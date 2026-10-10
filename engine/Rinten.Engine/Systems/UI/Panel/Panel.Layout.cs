using Rinten.Audio;

namespace Rinten.UI;

public partial class Panel
{
	internal PanelLayout LayoutTree;

	/// <summary>Access to various bounding boxes of this panel.</summary>
	[Hide]
	public Box Box { get; init; } = new Box();

	/// <summary>If true, calls <see cref="DrawContent(PanelRenderer, ref RenderState)"/>.</summary>
	[Hide, Obsolete( "Use Draw" )]
	public virtual bool HasContent => false;

	/// <summary>The velocity of the current scroll</summary>
	[Hide]
	public Vector2 ScrollVelocity;

	/// <summary>Whether a ScrollOffset write marks its chain for final layout and asks for a tick. <c>ui_scroll_marks_layout 0</c> leaves the write to the next unrelated layout, as before.</summary>
	[ConVar( "ui_scroll_marks_layout", Help = "A ScrollOffset write marks its chain for final layout and asks for a tick; 0 leaves the write to the next unrelated layout, as before" )]
	public static bool ScrollWriteMarksLayout { get; set; } = true;

	Vector2 scrollOffset;

	/// <summary>Offset of the panel's children position for scrolling purposes.</summary>
	[Hide]
	public Vector2 ScrollOffset
	{
		get => scrollOffset;
		set
		{
			if ( scrollOffset == value ) return;

			scrollOffset = value;

			if ( !ScrollWriteMarksLayout ) return;

			SetNeedsFinalLayout();
			RequestTick();
		}
	}

	/// <summary>Scale of the panel on the screen.</summary>
	[Hide]
	public float ScaleToScreen { get; internal set; } = 1.0f;

	/// <summary>Inverse scale of <see cref="ScaleToScreen"/>.</summary>
	[Hide]
	public float ScaleFromScreen => 1.0f / ScaleToScreen;

	int LayoutCount = 0;


	/// <summary>If this panel has transforms, they'll be reflected here</summary>
	[Hide]
	public Matrix? LocalMatrix { get; internal set; }

	/// <summary>If this panel or its parents have transforms, they'll be compounded here.</summary>
	[Hide]
	public Matrix? GlobalMatrix { get; internal set; }

	//The matrix that is applied as a result of transform: styles
	[Hide]
	internal Matrix TransformMatrix { get; set; }

	//The computed style has a non-default backdrop filter property
	[Hide]
	internal bool HasBackdropFilter { get; private set; }

	[Hide]
	internal bool HasFilter { get; private set; }

	[Hide]
	internal bool HasCustomDraw => CachedDescriptors?.CustomEntries.Count > 0;

	//The computed style has a renderable background
	[Hide]
	internal bool HasBackground { get; private set; }

	internal void UpdateVisibility()
	{
		IsVisibleSelf = ComputedStyle?.CalcVisible() ?? false;
		IsVisibleSelf = IsVisibleSelf || HasActiveTransitions;

		InheritVisibility();
	}

	//Updates visibility from own and parent style, propagating down; also run on reparent
	internal void InheritVisibility()
	{
		bool old = IsVisible;

		IsVisible = IsVisibleSelf && (Parent?.IsVisible ?? true);

		if ( old == IsVisible )
			return;

		MarkBoundsDirty();

		//A subtree entering or leaving the walk changes the picture without any panel's render version moving.
		MoveGeneration();

		//Shown again: children who asked while this was hidden were never reached, and the tick that consumed this panel's own mark did not descend.
		if ( IsVisible )
			RequestTick();

		if ( Parent != null )
		{
			Parent.IndexesDirty = true;
		}

		var c = _children?.Count ?? 0;

		for ( int i = 0; i < c; i++ )
		{
			_children[i].UpdateVisibility();
		}

		try
		{
			OnVisibilityChanged();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e );
		}
	}

	//Called when the visibility of the current panel changes. This could be because our own style
	//changed, or a parent style. You can check visibility using IsVisible and IsVisibleSelf.
	protected virtual void OnVisibilityChanged()
	{

	}

	bool needsPreLayout = true;
	bool needsFinalLayout = true;

	internal void SetNeedsPreLayout()
	{
		if ( needsPreLayout ) return;

		needsPreLayout = true;
		needsFinalLayout = true;

		Parent?.SetNeedsPreLayout();
	}

	//Marks this panel and every ancestor for final layout, without a style cascade - nothing about the styles changed, only where the children sit. The
	//whole chain is walked: a fresh panel is born marked while its ancestors are not, so a mark already standing here says nothing about the ones above it.
	internal void SetNeedsFinalLayout()
	{
		for ( var panel = this; panel is not null; panel = panel.Parent )
			panel.needsFinalLayout = true;
	}

	internal virtual void PreLayout( LayoutCascade cascade )
	{
		if ( LayoutTree == null || Style is null )
			return;

		if ( !needsPreLayout && !cascade.SelectorChanged && !cascade.ParentChanged && !LayoutTree.ReferenceSizeChanged )
			return;

		//Which of the three let it through. They mean different faults: "self" is a panel dirtying itself every frame, "selector" is one panel's
		//rules changing and the flag then travelling down to everything beneath it, and "parent" is a style that really did change being inherited.
		var why = needsPreLayout ? "pre cascade: self"
			: cascade.SelectorChanged ? "pre cascade: selector"
			: "pre cascade: parent";

		needsPreLayout = false;

		if ( IndexesDirty )
		{
			UpdateChildrenIndexes();
		}


		//The call count is the number that matters here: how many panels re-cascade in a frame. The guard above lets most of them
		//out, and until this is counted there is no telling whether PreLayout is expensive per panel or merely asked of too many.
		bool changed;

		using ( new TickZone( why ) )
		{
			ComputedStyle = Style.BuildFinal( ref cascade, out changed );
		}

		cascade.ParentStyles = ComputedStyle;

		//An animation advances only through the tick's dirty check, so one that is running asks for the tick.
		if ( ComputedStyle.IsAnimationActive )
			RequestTick();

		PushLengthValues();

		ScaleToScreen = cascade.Scale;
		var previousOpacity = Opacity;
		Opacity = ComputedStyle.Opacity.Value * (Parent?.Opacity ?? 1.0f);
		UpdateVisibility();

		if ( !LayoutTree.Initialized || LayoutTree.ReferenceSizeChanged || (changed && LayoutStyleChanged()) )
		{
			using var pushZone = new TickZone( "pre layout push" );
			UpdateLayoutStyle();
		}

		if ( Opacity != previousOpacity )
		{
			IsRenderDirty = true;
		}

		using var changedZone = new TickZone( changed ? "pre changed" : "pre unchanged" );

		if ( changed )
		{
			IsRenderDirty = true;

			if ( Parent is not null )
			{
				Parent._renderChildrenDirty = true;
			}

			HasBackdropFilter = !ComputedStyle.IsDefault( "backdrop-filter-blur" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-contrast" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-saturate" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-sepia" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-invert" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-hue-rotate" )
				|| !ComputedStyle.IsDefault( "backdrop-filter-brightness" );

			HasFilter = !ComputedStyle.IsDefault( "filter-saturate" )
				|| !ComputedStyle.IsDefault( "filter-brightness" )
				|| !ComputedStyle.IsDefault( "filter-contrast" )
				|| !ComputedStyle.IsDefault( "filter-blur" )
				|| !ComputedStyle.IsDefault( "filter-sepia" )
				|| !ComputedStyle.IsDefault( "filter-hue-rotate" )
				|| !ComputedStyle.IsDefault( "filter-invert" )
				|| !ComputedStyle.IsDefault( "filter-tint" )
				|| !ComputedStyle.IsDefault( "filter-border-width" );

			HasBackground = ComputedStyle.BackgroundColor.Value.a > 0f
				|| ComputedStyle.BorderImageSource is not null
				|| (ComputedStyle.BackgroundImage is not null && ComputedStyle.BackgroundImage != Texture.Invalid)
				|| (ComputedStyle.BorderLeftColor.Value.a > 0f && ComputedStyle.BorderLeftWidth.Value.GetPixels( 1.0f ) > 0f)
				|| (ComputedStyle.BorderTopColor.Value.a > 0f && ComputedStyle.BorderTopWidth.Value.GetPixels( 1.0f ) > 0f)
				|| (ComputedStyle.BorderRightColor.Value.a > 0f && ComputedStyle.BorderRightWidth.Value.GetPixels( 1.0f ) > 0f)
				|| (ComputedStyle.BorderBottomColor.Value.a > 0f && ComputedStyle.BorderBottomWidth.Value.GetPixels( 1.0f ) > 0f);

			UpdateLayer( ComputedStyle );
		}

		UpdateOrder();

		if ( LayoutCount > 0 && !IsVisibleSelf )
		{
			return;
		}

		if ( _children == null || _children.Count == 0 )
			return;

		//We need to tell the children to force an update if any of the parent's cascading styles have changed.
		cascade.ParentChanged = cascade.ParentChanged || changed;

		for ( int i = 0; i < _children.Count; i++ )
		{
			_children[i].PreLayout( cascade );
		}

		//Our children's 'order' properties might have changed if so, tell yoga about the new order
		SortChildrenOrder();
	}

	internal void UpdateLayoutStyle()
	{
		if ( ComputedStyle == null )
			return;

		LayoutTree.BeginPush();

		LayoutTree.Width = ComputedStyle.Width;
		LayoutTree.Height = ComputedStyle.Height;
		LayoutTree.MaxWidth = ComputedStyle.MaxWidth;
		LayoutTree.MaxHeight = ComputedStyle.MaxHeight;
		LayoutTree.MinWidth = ComputedStyle.MinWidth;
		LayoutTree.MinHeight = ComputedStyle.MinHeight;
		LayoutTree.Display = ComputedStyle.Display;

		LayoutTree.Left = ComputedStyle.Left;
		LayoutTree.Right = ComputedStyle.Right;
		LayoutTree.Top = ComputedStyle.Top;
		LayoutTree.Bottom = ComputedStyle.Bottom;

		LayoutTree.MarginLeft = ComputedStyle.MarginLeft;
		LayoutTree.MarginRight = ComputedStyle.MarginRight;
		LayoutTree.MarginTop = ComputedStyle.MarginTop;
		LayoutTree.MarginBottom = ComputedStyle.MarginBottom;

		LayoutTree.PaddingLeft = ComputedStyle.PaddingLeft;
		LayoutTree.PaddingRight = ComputedStyle.PaddingRight;
		LayoutTree.PaddingTop = ComputedStyle.PaddingTop;
		LayoutTree.PaddingBottom = ComputedStyle.PaddingBottom;

		LayoutTree.BorderLeftWidth = ComputedStyle.BorderLeftWidth;
		LayoutTree.BorderTopWidth = ComputedStyle.BorderTopWidth;
		LayoutTree.BorderRightWidth = ComputedStyle.BorderRightWidth;
		LayoutTree.BorderBottomWidth = ComputedStyle.BorderBottomWidth;

		LayoutTree.Gutter = ScrollbarGutter;

		LayoutTree.PositionType = ComputedStyle.Position;
		LayoutTree.BoxSizing = ComputedStyle.BoxSizing;
		LayoutTree.GridTemplateColumns = ComputedStyle.GridTemplateColumns;
		LayoutTree.GridTemplateRows = ComputedStyle.GridTemplateRows;
		LayoutTree.GridAutoColumns = ComputedStyle.GridAutoColumns;
		LayoutTree.GridAutoRows = ComputedStyle.GridAutoRows;
		LayoutTree.GridAutoFlow = ComputedStyle.GridAutoFlow;
		LayoutTree.GridColumnStart = ComputedStyle.GridColumnStart;
		LayoutTree.GridColumnEnd = ComputedStyle.GridColumnEnd;
		LayoutTree.GridRowStart = ComputedStyle.GridRowStart;
		LayoutTree.GridRowEnd = ComputedStyle.GridRowEnd;
		LayoutTree.JustifyItems = ComputedStyle.JustifyItems;
		LayoutTree.JustifySelf = ComputedStyle.JustifySelf;
		LayoutTree.AspectRatio = ComputedStyle.AspectRatio;
		LayoutTree.FlexGrow = ComputedStyle.FlexGrow;
		LayoutTree.FlexShrink = ComputedStyle.FlexShrink;
		LayoutTree.FlexBasis = ComputedStyle.FlexBasis;
		LayoutTree.Wrap = ComputedStyle.FlexWrap;

		LayoutTree.AlignContent = ComputedStyle.AlignContent;
		LayoutTree.AlignItems = ComputedStyle.AlignItems;
		LayoutTree.AlignSelf = ComputedStyle.AlignSelf;
		LayoutTree.FlexDirection = ComputedStyle.FlexDirection;
		LayoutTree.JustifyContent = ComputedStyle.JustifyContent;
		LayoutTree.Overflow = ComputedStyle.Overflow;

		LayoutTree.RowGap = ComputedStyle.RowGap;
		LayoutTree.ColumnGap = ComputedStyle.ColumnGap;

		LayoutTree.EndPush();
	}

	/// <summary>The currently calculated opacity. This is set by multiplying our current style opacity with our parent's opacity.</summary>
	[Hide]
	public float Opacity { get; private set; } = 1.0f;

	/// <summary>This panel has just been laid out. You can modify its position now and it will affect its children. This is a useful place to restrict shit to the screen etc.</summary>
	public virtual void OnLayout( ref Rect layoutRect )
	{

	}

	int layoutHash;

	/// <summary>Takes a <see cref="LayoutCascade"/> and returns an outer rect</summary>
	public virtual void FinalLayout( Vector2 offset )
	{
		if ( ComputedStyle is null )
			return;

		if ( LayoutTree is null )
			return;

		PushLengthValues();

		var hash = HashCode.Combine( offset, ScrollOffset, ScrollVelocity, ComputedStyle?.Transform, Opacity, ComputedStyle.Display );
		if ( layoutHash == hash && !LayoutTree.HasNewLayout && !needsFinalLayout ) return;

		needsFinalLayout = false;
		layoutHash = hash;

		MarkBoundsDirty();

		//if ( LayoutTree.HasNewLayout || parentPos != offset )
		{
			var previousRect = Box.Rect;

			Box.Rect = LayoutTree.LayoutRect;

			Box.Rect.Position += offset;

			OnLayout( ref Box.Rect );

			Box.Padding = LayoutTree.Padding;
			Box.Margin = LayoutTree.Margin;

			//The scrollbar gutter rides on the layout's border, but it's inside the clip and doesn't draw
			var border = LayoutTree.Border;
			var gutter = LayoutTree.Gutter;
			Box.Border = new Margin( border.Left - gutter.Left, border.Top, border.Right - gutter.Right, border.Bottom );

			Box.RectOuter = Box.Rect.Grow( LayoutTree.Margin.Left, LayoutTree.Margin.Top, LayoutTree.Margin.Right, LayoutTree.Margin.Bottom );
			Box.RectInner = Box.Rect.Shrink( LayoutTree.Padding.Left, LayoutTree.Padding.Top, LayoutTree.Padding.Right, LayoutTree.Padding.Bottom );
			Box.ClipRect = Box.Rect.Shrink( Box.Border.Left, Box.Border.Top, Box.Border.Right, Box.Border.Bottom );

			UpdateLayer( ComputedStyle );

			Box.Rect = Box.Rect.Floor();
			Box.RectOuter = Box.RectOuter.Floor();
			Box.RectInner = Box.RectInner.Floor();
			Box.ClipRect = Box.ClipRect.Floor();

			//Build the matrix that is generated from "transform" etc. We do this here after we have the size of the panel - which should be super duper fine.
			TransformMatrix = ComputedStyle.BuildTransformMatrix( Box.Rect.Size );

			if ( previousRect.Size != Box.Rect.Size ) MarkSizeDependentChildren();

			if ( previousRect != Box.Rect )
			{
				IsRenderDirty = true;
			}
		}

		//If we have an intro flag, we need to turn it off because by now it's been on for one frame
		if ( HasIntro )
		{
			//A nice optimization here would be to not dirty the style selector if none of our styles have a :intro flag
			Switch( PseudoClass.Intro, false );
		}

		if ( ComputedStyle.Display == DisplayMode.None ) return;
		if ( LayoutCount > 0 && Opacity <= 0.0f ) return;

		//The initial state should be true for these panels So there is no need to manually scroll to the bottom for scroll to be pinned there by default
		if ( LayoutCount == 0 && PreferScrollToBottom )
		{
			IsScrollAtBottom = true;
		}

		bool wasScrollatBottom = IsScrollAtBottom;

		_laidOutScrollOffset = ScrollOffset.SnapToGrid( 1.0f );
		offset = Box.Rect.Position - _laidOutScrollOffset;
		FinalLayoutChildren( offset );

		if ( wasScrollatBottom )
		{
			UpdateScrollPin();
		}

		LayoutCount++;
	}

	private void PushLengthValues()
	{
		Length.CurrentFontSize = ComputedStyle.FontSize ?? Length.Pixels( 13 ).Value;
	}

	//The scroll offset the children were last laid out against
	Vector2 _laidOutScrollOffset;

	/// <summary>If true, we'll try to stay scrolled to the bottom when the panel changes size</summary>
	[Hide]
	public bool PreferScrollToBottom { get; set; }

	/// <summary>Whether the scrolling is currently pinned to the bottom of the panel as dictated by <see cref="PreferScrollToBottom"/>.</summary>
	[Hide]
	public bool IsScrollAtBottom { get; private set; }

	/// <summary>The size of the scrollable area within this panel.</summary>
	[Hide]
	public Vector2 ScrollSize { get; private set; }

	/// <summary>Is this panel currently being scrolled by dragging?</summary>
	[Hide]
	public bool IsDragScrolling { get; private set; }

	//Layout the children of this panel.
	protected virtual void FinalLayoutChildren( Vector2 offset )
	{
		if ( !HasChildren )
			return;

		for ( int i = 0; i < _children.Count; i++ )
		{
			try
			{
				_children[i].FinalLayout( offset );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}

		if ( ComputedStyle.Overflow.Value == OverflowMode.Scroll )
		{
			var rect = Box.Rect;
			rect.Position -= ScrollOffset;

			//Scroll area is the box grown to fit children; trailing padding goes on the children's extent
			Rect content = default;
			bool hasContent = false;

			for ( int i = 0; i < _children.Count; i++ )
			{
				var child = _children[i];

				if ( !child.IsVisible )
					continue;

				if ( child is ScrollBar )
					continue;

				var childRect = child.GetLayoutRect();

				if ( hasContent ) content.Add( childRect );
				else content = childRect;

				hasContent = true;
			}

			if ( hasContent )
			{
				//Content scrolls up to the gutter, not under it
				content.Right += Box.Padding.Right + (LayoutTree?.Gutter.Right ?? 0);
				content.Bottom += Box.Padding.Bottom;
				rect.Add( content );
			}

			ConstrainScrolling( rect.Size );
		}
		else
		{
			ScrollOffset = 0;
		}

	}

	Rect GetLayoutRect()
	{
		//A panel added since the last time styles were computed has none yet - and no box worth speaking of either, so it is asked for what it has.
		if ( HasChildren && ComputedStyle?.Display == DisplayMode.Contents )
		{
			Rect rect = default;
			for ( int i = 0; i < _children.Count; i++ )
			{
				var child = _children[i];

				if ( child.IsVisible )
				{
					if ( i == 0 ) rect = child.GetLayoutRect();
					else rect.Add( child.GetLayoutRect() );
				}
			}

			return rect;
		}

		return Box.RectOuter;
	}

	private void UpdateScrollPin()
	{
		if ( !PreferScrollToBottom )
			return;

		if ( IsScrollAtBottom )
			return;

		if ( !ScrollVelocity.y.AlmostEqual( 0, 0.1f ) )
			return;

		ScrollOffset = new Vector2( ScrollOffset.x, ScrollSize.y );
		IsScrollAtBottom = true;
		ScrollVelocity.y = 0;
		scrollVelocityVelocity.y = 0;
	}

	bool isScrolling;
	Vector2 scrollVelocityVelocity;

	internal void StopScrollVelocity()
	{
		ScrollVelocity = 0;
		scrollVelocityVelocity = 0;
	}

	protected virtual void AddScrollVelocity()
	{
		if ( ScrollVelocity.IsNearZeroLength )
		{
			ScrollVelocity = 0;
			return;
		}

		ScrollVelocity = Vector2.SmoothDamp( ScrollVelocity, 0, ref scrollVelocityVelocity, 0.5f, RealTime.SmoothDelta );

		//Bring it to a stop
		if ( ScrollVelocity.y.AlmostEqual( 0, 0.01f ) ) ScrollVelocity.y = 0;
		if ( ScrollVelocity.x.AlmostEqual( 0, 0.01f ) ) ScrollVelocity.x = 0;
	}

	//Reversed by flex-direction: *-reverse or justify-content: flex-end, when the scroll offset runs from -ScrollSize to zero
	internal bool IsScrollAxisReversed => ComputedStyle.JustifyContent == Justify.FlexEnd || ComputedStyle.FlexDirection == FlexDirection.RowReverse || ComputedStyle.FlexDirection == FlexDirection.ColumnReverse;

	//Constrain scrolling to the given size.
	protected virtual void ConstrainScrolling( Vector2 size )
	{
		if ( IsDragScrolling )
			return;

		isScrolling = false;

		size -= Box.Rect.Size;

		var heightChange = size.y - ScrollSize.y;

		ScrollSize = size;
		ScrollSize = ScrollSize.SnapToGrid( 1.0f );

		//The scrollbars are made and dropped in the tick, and whether one is wanted is only
		//known now - so a panel whose bars no longer match what it scrolls asks for one.
		if ( ScrollbarsOutOfDate() ) RequestTick();

		var overflow = ComputedStyle.Overflow;

		if ( overflow == OverflowMode.Visible || overflow == OverflowMode.Hidden )
		{
			ScrollOffset = 0;
			StopScrollVelocity();
			return;
		}

		var so = ScrollOffset;

		//add velocity
		so += ScrollVelocity * RealTime.SmoothDelta * 60.0f;

		var axisReversed = IsScrollAxisReversed;

		IsScrollAtBottom = so.y + ScrollVelocity.y >= size.y;
		if ( ScrollVelocity.y > 0 && IsScrollAtBottom ) so.y += heightChange;

		//Preserve the original spring and inertia; only constrain its outermost extent.
		var min = axisReversed ? -ScrollSize : Vector2.Zero;
		var max = axisReversed ? Vector2.Zero : ScrollSize;
		var constrainSpeed = RealTime.SmoothDelta * 100.0f;
		so.x = so.x.LerpTo( so.x.Clamp( min.x, max.x ), constrainSpeed );
		so.y = so.y.LerpTo( so.y.Clamp( min.y, max.y ), constrainSpeed );

		//overscroll-behavior: none has no bounce at all
		var limit = ScrollBounceLimit;
		if ( ComputedStyle.OverscrollBehaviorX == OverscrollBehavior.None ) limit.x = 0;
		if ( ComputedStyle.OverscrollBehaviorY == OverscrollBehavior.None ) limit.y = 0;
		var constrained = so.Clamp( min - limit, max + limit );
		if ( (constrained.x <= min.x - limit.x && ScrollVelocity.x < 0) || (constrained.x >= max.x + limit.x && ScrollVelocity.x > 0) )
		{
			ScrollVelocity.x = 0;
			scrollVelocityVelocity.x = 0;
		}
		if ( (constrained.y <= min.y - limit.y && ScrollVelocity.y < 0) || (constrained.y >= max.y + limit.y && ScrollVelocity.y > 0) )
		{
			ScrollVelocity.y = 0;
			scrollVelocityVelocity.y = 0;
		}
		so = constrained;

		if ( ScrollOffset == so )
			return;

		ScrollOffset = so;
		isScrolling = true;
		RequestTick();
	}

	//The hard limit on edge bounce, in screen pixels: 20% of each viewport axis, capped at 150 UI pixels.
	Vector2 ScrollBounceLimit => Vector2.Min( Box.Rect.Size * 0.2f, new Vector2( 150, 150 ) * ScaleToScreen );

	/// <summary>Play a sound from this panel.</summary>
	public void PlaySound( string sound )
	{
		if ( string.IsNullOrEmpty( sound ) )
			return;

		var h = Sound.Play( sound );
		if ( !h.IsValid() )
			return;

		if ( FindRootPanel() is WorldPanel worldPanel )
		{
			//Calculate world position of the element, not just the root WorldPanel
			var worldPosition = worldPanel.Position;
			var panelPosition = Box.Rect.Position;
			var worldRotation = worldPanel.Rotation * Rotation.FromYaw( 180 );
			var worldOffset = new Vector3( panelPosition.x, panelPosition.y, 0 );
			worldOffset = worldRotation * (worldOffset * ScenePanelObject.ScreenToWorldScale);
			h.TargetMixer = Mixer.FindMixerByName( "Game" );
			h.Position = worldPosition + worldOffset;
		}
		else
		{
			var normalizedScreenPosition = Box.Rect.Center / Screen.Size;
			normalizedScreenPosition -= 0.5f;
			h.TargetMixer = Mixer.FindMixerByName( "UI" );
			//In front of the listener, which is -z, with the screen's x across and its y - which grows downwards - up.
			h.Position = new Vector3( normalizedScreenPosition.x.Clamp( -1, 1 ) * 256.0f,
				-normalizedScreenPosition.y.Clamp( -1, 1 ) * 64.0f, -64.0f );
			h.ListenLocal = true;
		}
	}


	//Screen rect this subtree can draw into, or null if unboundable; lets rendering skip clipped subtrees
	internal Rect? SubtreeBounds { get; private set; }

	//Whether something under here moved, showed or hid since the bounds were last taken. On from birth.
	bool boundsDirty = true;

	//Up to the root, stopping where an ancestor already knows: a bound is only re-taken along a chain that is marked.
	void MarkBoundsDirty()
	{
		for ( var panel = this; panel is not null && !panel.boundsDirty; panel = panel.Parent )
			panel.boundsDirty = true;
	}

	//Unions this panel's own extent with its children's, bottom up, along the chains
	//FinalLayout and a visibility change marked; a clean subtree keeps its bounds.
	internal void UpdateSubtreeBounds()
	{
		//Every change a bound depends on - a rect, a shadow, a transform, a wall, a child shown or hidden -
		//runs FinalLayout's body or UpdateVisibility on the panel, and both mark the chain up to the root.
		if ( !boundsDirty ) return;

		boundsDirty = false;

		var own = DrawnExtent();
		var bounds = own ?? default;
		var bounded = own.HasValue;

		//Overflow-hidden panels clip children, so they stay bounded whatever their children draw
		var walls = bounded && ComputedStyle is
		{
			OverflowX: OverflowMode.Hidden or OverflowMode.Scroll
		} or
		{
			OverflowY: OverflowMode.Hidden or OverflowMode.Scroll
		};

		if ( _children is not null )
		{
			for ( int i = 0; i < _children.Count; i++ )
			{
				var child = _children[i];
				if ( child is null ) continue;

				child.UpdateSubtreeBounds();

				//Still walked, because its own children need their bounds either way - it just contributes nothing to this one.
				if ( walls ) continue;
				if ( !child.IsVisible ) continue;

				if ( !bounded ) continue;

				if ( child.SubtreeBounds is not { } childBounds )
				{
					bounded = false;
					continue;
				}

				bounds.Add( childBounds );
			}
		}

		SubtreeBounds = bounded ? bounds : null;
	}

	//Where this panel alone can put pixels: its box, grown by whatever its shadows throw past it. Null when nothing can say where it draws.
	private Rect? DrawnExtent()
	{
		//A transformed panel is not where its box says it is, and the walk hands the same transform to everything below it.
		if ( TransformMatrix != Matrix.Identity )
			return null;

		//And one that draws its own quads can put them anywhere at all - as does a vector shape, which is placed in its view box rather than in its box.
		if ( DrawsItsOwn || this is SvgElement )
			return null;

		var rect = Box.Rect;

		if ( ComputedStyle?.BoxShadow is not { Count: > 0 } shadows )
			return rect;

		//The same reach the walk gives a shadow's quad: its spread and blur, two more for the blur's tail, thrown by wherever the shadow is offset to.
		for ( int i = 0; i < shadows.Count; i++ )
		{
			var shadow = shadows[i];
			if ( shadow.Inset || shadow.Color.a <= 0.001f ) continue;

			var grow = shadow.Spread + shadow.Blur + 2.0f;

			rect.Add( new Rect(
				Box.Rect.Left + shadow.OffsetX - grow,
				Box.Rect.Top + shadow.OffsetY - grow,
				Box.Rect.Width + ( grow * 2.0f ),
				Box.Rect.Height + ( grow * 2.0f ) ) );
		}

		return rect;
	}

	//Whether OnDraw paints quads of its own; panels drawn by the walk (DrawnByWalk) don't count
	private bool DrawsItsOwn
	{
		get
		{
			if ( DrawnByWalk ) return false;

			//Asked of every panel every frame, so the answer is kept on the panel and the table is only read when its type is new to it.
			var type = GetType();

			if ( !ReferenceEquals( type, drawsItsOwnType ) )
			{
				drawsItsOwnType = type;
				drawsItsOwnCached = drawsItsOwn.GetOrAdd( type, static t =>
					t.GetMethod( nameof( OnDraw ),
						System.Reflection.BindingFlags.Instance
						| System.Reflection.BindingFlags.Public
						| System.Reflection.BindingFlags.NonPublic )?.DeclaringType != typeof( Panel ) );
			}

			return drawsItsOwnCached;
		}
	}

	private System.Type drawsItsOwnType;
	private bool drawsItsOwnCached;

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<System.Type, bool> drawsItsOwn = new();
}

/// <summary>Represents position and size of a <see cref="Panel"/> on the screen.</summary>
[SkipHotload]
public class Box
{
	/// <summary>Position and size of the element on the screen, <b>including both - its padding AND margin</b>.</summary>
	public Rect RectOuter;

	/// <summary>Position and size of only the element's inner content on the screen, <i>without padding OR margin</i>.</summary>
	public Rect RectInner;

	/// <summary>The size of padding.</summary>
	public Margin Padding;

	/// <summary>The size of border.</summary>
	public Margin Border;

	/// <summary>The size of border.</summary>
	public Margin Margin;

	/// <summary>Position and size of the element on the screen, <b>including its padding</b>, <i>but not margin</i>.</summary>
	public Rect Rect;

	/// <summary><see cref="Rect"/> minus the border sizes. Used internally to "clip" (hide) everything outside of these bounds, if the panels <see cref="OverflowMode"/> is not set to <see cref="OverflowMode.Visible"/>.</summary>
	public Rect ClipRect;

	/// <summary>Position of the left edge in screen coordinates.</summary>
	public float Left => Rect.Left;

	/// <summary>Position of the right edge in screen coordinates.</summary>
	public float Right => Rect.Right;

	/// <summary>Position of the top edge in screen coordinates.</summary>
	public float Top => Rect.Top;

	/// <summary>Position of the bottom edge in screen coordinates.</summary>
	public float Bottom => Rect.Bottom;
}

internal static class YogaEx
{
	public static float ToFloat( this Length? self, Length? dimension )
	{
		if ( self == null ) return 0;

		if ( self.Value.Unit == LengthUnit.Expression )
			return self.Value.GetPixels( dimension?.Value ?? 0f );

		if ( self.Value.Unit == LengthUnit.Pixels )
			return self.Value.Value;

		if ( self.Value.Unit == LengthUnit.RootEm || self.Value.Unit == LengthUnit.Em )
			return self.Value.GetPixels( dimension?.Value ?? 0f );

		//TODO
		return self.Value.Value;
	}
}
