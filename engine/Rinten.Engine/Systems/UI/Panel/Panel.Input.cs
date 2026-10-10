namespace Rinten.UI;


public partial class Panel
{
	/// <summary>Current mouse position local to this panels top left corner.</summary>
	[Hide]
	public Vector2 MousePosition
	{
		get
		{
			if ( FindRootPanel() is not RootPanel root )
				return default;

			return ToLaidOutSpace( root.MousePos ) - Box.Rect.Position;
		}

	}

	//This panel's own transform from style, the same way CoreUiRenderer applies it, or null if none
	internal Matrix? OwnTransform()
	{
		if ( TransformMatrix == Matrix.Identity || ComputedStyle is not { } style ) return null;

		var box = Box.Rect;

		var origin = new Vector3(
			box.Left + (style.TransformOriginX?.GetPixels( box.Width ) ?? box.Width * 0.5f),
			box.Top + (style.TransformOriginY?.GetPixels( box.Height ) ?? box.Height * 0.5f),
			0.0f );

		return Matrix.CreateTranslation( -origin ) * TransformMatrix * Matrix.CreateTranslation( origin );
	}

	//Maps a root-space point into this panel's layout space; uses GlobalMatrix or walks the styles
	internal Vector2 ToLaidOutSpace( Vector2 point )
	{
		if ( GlobalMatrix is { } global ) return global.Transform( point );

		if ( Parent is not null ) point = Parent.ToLaidOutSpace( point );

		return OwnTransform()?.Inverted.Transform( point ) ?? point;
	}

	//The other way: a point in the space this panel was laid out in, taken up through its own transform and every one above it, into the root's space.
	internal Vector2 ToDrawnSpace( Vector2 point )
	{
		if ( GlobalMatrix is { } global ) return global.Inverted.Transform( point );

		if ( OwnTransform() is { } own ) point = own.Transform( point );

		return Parent?.ToDrawnSpace( point ) ?? point;
	}

	/// <summary>The panel under a point, using the same walk as hover so it matches what gets clicked</summary>
	public Panel PanelUnder( Vector2 point, System.Func<Panel, bool> skip = null, System.Func<Panel, bool> selectable = null )
	{
		Panel found = null;

		PanelInput.Search( this, point, skip, selectable, ref found );

		return found;
	}

	/// <summary>Called by <c>PanelInput.Search</c> to transform the current mouse position using the panel's LocalMatrix (by default). This can be overriden for special cases.</summary>
	public virtual Vector2 GetTransformPosition( Vector2 pos )
	{
		//The walk arrives here with the point already taken through every transform above this panel, so only this panel's own is left to undo.
		if ( LocalMatrix is { } local ) return local.Transform( pos );

		return OwnTransform()?.Inverted.Transform( pos ) ?? pos;
	}

	//Whether a point is inside this panel by a shape of its own rather than by its box,
	//or null for a panel that is its box. The point is in the same space as IsInside's.
	internal virtual bool? InsideShape( Vector2 pos ) => null;

	/// <summary>Whether given screen position is within this panel. This will accurately handle border radius as well.</summary>
	public bool IsInside( Vector2 pos )
	{
		//A shape of its own - a vector shape in an svg - is hit where it is drawn, not by its box.
		if ( InsideShape( pos ) is { } shaped ) return shaped;

		var rect = Box.Rect;

		if ( pos.x < rect.Left || pos.x > rect.Right ) return false;
		if ( pos.y < rect.Top || pos.y > rect.Bottom ) return false;

		var s = ComputedStyle;
		if ( s == null ) return false;

		pos.x -= rect.Left;
		pos.y -= rect.Top;


		if ( s.BorderTopLeftRadius.HasValue && s.BorderTopLeftRadius.Value.Unit > 0 )
		{
			var r = s.BorderTopLeftRadius.Value.GetPixels( (rect.Width + rect.Height) * 0.5f );
			r = MathF.Min( MathF.Min( r, rect.Width / 2.0f ), rect.Height / 2.0f );
			var c = new Vector2( r, r );
			if ( pos.x < c.x && pos.y < c.y && Vector2.Distance( pos, c ) > r )
				return false;
		}

		if ( s.BorderTopRightRadius.HasValue && s.BorderTopRightRadius.Value.Unit > 0 )
		{
			var r = s.BorderTopRightRadius.Value.GetPixels( (rect.Width + rect.Height) * 0.5f );
			r = MathF.Min( MathF.Min( r, rect.Width / 2.0f ), rect.Height / 2.0f );
			var c = new Vector2( rect.Width - r, r );
			if ( pos.x > c.x && pos.y < c.y && Vector2.Distance( pos, c ) > r )
				return false;
		}

		if ( s.BorderBottomRightRadius.HasValue && s.BorderBottomRightRadius.Value.Unit > 0 )
		{
			var r = s.BorderBottomRightRadius.Value.GetPixels( (rect.Width + rect.Height) * 0.5f );
			r = MathF.Min( MathF.Min( r, rect.Width / 2.0f ), rect.Height / 2.0f );
			var c = new Vector2( rect.Width - r, rect.Height - r );
			if ( pos.x > c.x && pos.y > c.y && Vector2.Distance( pos, c ) > r )
				return false;
		}

		if ( s.BorderBottomLeftRadius.HasValue && s.BorderBottomLeftRadius.Value.Unit > 0 )
		{
			var r = s.BorderBottomLeftRadius.Value.GetPixels( (rect.Width + rect.Height) * 0.5f );
			r = MathF.Min( MathF.Min( r, rect.Width / 2.0f ), rect.Height / 2.0f );
			var c = new Vector2( r, rect.Height - r );
			if ( pos.x < c.x && pos.y > c.y && Vector2.Distance( pos, c ) > r )
				return false;
		}

		return true;
	}

	/// <summary>Whether the given rect is inside this panels bounds. (<see cref="Box.Rect"/>)</summary>
	public bool IsInside( Rect rect, bool fullyInside )
	{
		return rect.IsInside( Box.Rect, fullyInside );
	}

	/// <summary>False by default, can this element accept keyboard focus. If an element accepts focus it'll be able to receive keyboard input.</summary>
	[Property]
	public bool AcceptsFocus { get; set; }

	/// <summary>When false, a mouse press doesn't focus this panel; only Tab or Focus() does</summary>
	[Property]
	public bool FocusOnPress { get; set; } = true;

	/// <summary>Keyboard input mode when focused: UI gets typed keys, Game forwards them as game buttons</summary>
	[Property]
	public PanelInputType ButtonInput { get; set; }

	/// <summary>False by default. Anything that is capable of accepting IME input should return true. Which is probably just a TextEntry.</summary>
	[Hide]
	public virtual bool AcceptsImeInput => false;

	/// <summary>Give input focus to this panel.</summary>
	public bool Focus()
	{
		return InputFocus.Set( this );
	}

	/// <summary>Remove input focus from this panel.</summary>
	public bool Blur()
	{
		return InputFocus.Clear( this );
	}

	/// <summary>Where this panel sits in the Tab order. 0, the default, is tree order. Positive values come before that, lowest first. Negative means Tab skips it, though it can still be focused by clicking or <see cref="Focus"/>.</summary>
	[Property]
	public int TabIndex { get; set; }

	/// <summary>Move focus to the panel after this one in Tab order.</summary>
	public bool FocusNext()
	{
		return Rinten.Engine.GlobalContext.Current.UISystem?.MoveFocus( this, false ) ?? false;
	}

	/// <summary>Move focus to the panel before this one in Tab order.</summary>
	public bool FocusPrevious()
	{
		return Rinten.Engine.GlobalContext.Current.UISystem?.MoveFocus( this, true ) ?? false;
	}

	//Enter or Space on a focused control clicks it. Returns true when the key was one of
	//those and the click was sent. Only the press counts - OnButtonTyped gets the release too.
	protected bool TryClickFromKeyboard( ButtonEvent e )
	{
		if ( !e.Pressed ) return false;
		if ( e.Button is not ("enter" or "space") ) return false;

		CreateEvent( new MousePanelEvent( "onclick", this, "mouseleft" ) );
		return true;
	}

	//Scroll every scrolling ancestor the least amount that brings this panel into view.
	internal void ScrollAncestorsIntoView()
	{
		for ( var p = Parent; p is not null; p = p.Parent )
		{
			p.ScrollIntoView( Box.Rect );
		}
	}

	/// <summary>Called when any button, mouse (except for mouse4/5) and keyboard, are pressed or depressed while hovering this panel.</summary>
	public virtual void OnButtonEvent( ButtonEvent e )
	{
		Parent?.OnButtonEvent( e );
	}

	/// <summary>Called when a printable character has been typed (pressed) while this panel has input focus. (<see cref="Focus"/>)</summary>
	public virtual void OnKeyTyped( char k )
	{
		Parent?.OnKeyTyped( k );
	}

	/// <summary>Called when any keyboard button is typed while this panel has input focus. (<see cref="Focus"/>)</summary>
	public virtual void OnButtonTyped( ButtonEvent e )
	{
		Parent?.OnButtonTyped( e );
	}

	/// <summary>Called when the user presses CTRL+V while this panel has input focus.</summary>
	public virtual void OnPaste( string text )
	{
		Parent?.OnPaste( text );
	}

	/// <summary>If we have a value that can be copied to the clipboard, return it here.</summary>
	public virtual string GetClipboardValue( bool cut )
	{
		if ( AllowChildSelection )
			return CollectSelectedChildrenText( this );

		if ( Parent != null )
			return Parent.GetClipboardValue( cut );

		return null;
	}

	/// <summary>Called when the player scrolls their mouse wheel while hovering this panel.</summary>
	public virtual void OnMouseWheel( Vector2 value )
	{
		TryScroll( value, out var remaining );
		if ( remaining != Vector2.Zero ) Parent?.OnMouseWheel( remaining );
	}

	/// <summary>Called from <see cref="OnMouseWheel"/> to try to scroll.</summary>
	public bool TryScroll( Vector2 value ) => TryScroll( value, out _ );

	bool TryScroll( Vector2 value, out Vector2 remaining )
	{
		remaining = value;
		if ( ComputedStyle == null ) return false;

		var velocityAdd = Vector2.Zero;
		if ( ConsumeWheelAxis( -value.x, true ) )
		{
			remaining.x = 0;
			if ( HasScrollX ) velocityAdd.x = value.x * -20;
		}
		if ( ConsumeWheelAxis( value.y, false ) )
		{
			remaining.y = 0;
			if ( HasScrollY ) velocityAdd.y = value.y * 20;
		}

		velocityAdd *= (1 + ScrollVelocity.Length / 100.0f);
		ScrollVelocity += velocityAdd;

		//Keep pathological event bursts finite without changing ordinary wheel acceleration.
		var maxVelocity = 1000000.0f * ScaleToScreen;
		if ( velocityAdd.x != 0 ) ScrollVelocity.x = ScrollVelocity.x.Clamp( -maxVelocity, maxVelocity );
		if ( velocityAdd.y != 0 ) ScrollVelocity.y = ScrollVelocity.y.Clamp( -maxVelocity, maxVelocity );

		if ( !velocityAdd.IsNearZeroLength )
			RequestTick();

		return remaining != value;
	}

	bool ConsumeWheelAxis( float direction, bool horizontal )
	{
		if ( direction == 0 || !IsScrollContainer( horizontal ) ) return false;
		if ( GetOverscrollBehavior( horizontal ) != OverscrollBehavior.Auto ) return true;
		if ( CanScrollInDirection( direction, horizontal ) ) return true;
		if ( FindScrollChainTarget( direction, horizontal ) != null ) return false;

		//The last scrollable container keeps the local boundary bounce.
		return horizontal ? HasScrollX : HasScrollY;
	}

	bool IsScrollContainer( bool horizontal ) => (horizontal ? ComputedStyle?.OverflowX : ComputedStyle?.OverflowY) == OverflowMode.Scroll;

	OverscrollBehavior GetOverscrollBehavior( bool horizontal ) =>
		(horizontal ? ComputedStyle?.OverscrollBehaviorX : ComputedStyle?.OverscrollBehaviorY) ?? OverscrollBehavior.Auto;

	bool CanScrollInDirection( float direction, bool horizontal )
	{
		var extent = horizontal ? ScrollSize.x : ScrollSize.y;
		if ( extent <= 0 ) return false;

		var offset = horizontal ? ScrollOffset.x : ScrollOffset.y;
		var min = IsScrollAxisReversed ? -extent : 0;
		var max = IsScrollAxisReversed ? 0 : extent;
		return direction < 0 ? offset > min : offset < max;
	}

	Panel FindScrollChainTarget( float direction, bool horizontal )
	{
		for ( var panel = Parent; panel != null; panel = panel.Parent )
		{
			if ( !panel.IsScrollContainer( horizontal ) ) continue;
			if ( panel.GetOverscrollBehavior( horizontal ) != OverscrollBehavior.Auto || panel.CanScrollInDirection( direction, horizontal ) )
				return panel;
		}

		return null;
	}

	/// <summary>Scroll to the bottom, if the panel has scrolling enabled.</summary>
	public bool TryScrollToBottom()
	{
		if ( ComputedStyle == null ) return false;
		if ( !HasScrollY ) return false;

		ScrollOffset = new Vector2( ScrollOffset.x, ScrollSize.y );
		IsScrollAtBottom = true;
		StopScrollVelocity();
		RequestTick();
		return true;
	}

	/// <summary>Jump to a scroll position, clamped to the scrollable area. Stops any inertia.</summary>
	public void ScrollTo( Vector2 offset )
	{
		var min = IsScrollAxisReversed ? -ScrollSize : Vector2.Zero;
		var max = IsScrollAxisReversed ? Vector2.Zero : ScrollSize;

		offset = Vector2.Max( Vector2.Min( offset, max ), min );

		StopScrollVelocity();
		IsScrollAtBottom = offset.y >= ScrollSize.y;

		if ( ScrollOffset == offset )
			return;

		ScrollOffset = offset;
		SetNeedsFinalLayout();
	}

	/// <summary>Scroll the least amount that brings a screen-space rect inside the content box. Returns false if nothing scrolled. The offset isn't clamped here: content that has just grown hasn't updated the scroll range yet, and the next layout clamps anyway.</summary>
	public bool ScrollIntoView( Rect rect )
	{
		if ( ComputedStyle is null ) return false;
		if ( ComputedStyle.Overflow != OverflowMode.Scroll ) return false;

		var view = Box.RectInner;
		var clip = ContentClipRect;
		view.Left = Math.Max( view.Left, clip.Left );
		view.Right = Math.Min( view.Right, clip.Right );

		var offset = ScrollOffset;

		//A scroll requested but not yet laid out will move the rect, so work from where it's going to be
		rect.Position -= offset - _laidOutScrollOffset;

		if ( ComputedStyle.OverflowY == OverflowMode.Scroll )
		{
			if ( rect.Bottom > view.Bottom ) offset.y += MathF.Ceiling( rect.Bottom - view.Bottom );
			if ( rect.Top < view.Top ) offset.y -= MathF.Ceiling( view.Top - rect.Top );
		}

		if ( ComputedStyle.OverflowX == OverflowMode.Scroll )
		{
			if ( rect.Right > view.Right ) offset.x += MathF.Ceiling( rect.Right - view.Right );
			if ( rect.Left < view.Left ) offset.x -= MathF.Ceiling( view.Left - rect.Left );
		}

		if ( offset == ScrollOffset ) return false;

		StopScrollVelocity();
		ScrollOffset = offset;
		SetNeedsFinalLayout();
		return true;
	}

	internal static Panel MouseCapture { get; private set; }

	/// <summary>Captures the mouse cursor while active. The cursor will be hidden and will be stuck in place. You will want to use <see cref="Mouse.Delta"/> in <see cref="Panel.Tick"/> while <see cref="HasMouseCapture"/> to read mouse movements. You can call this from <see cref="OnButtonEvent"/> for mouse clicks.</summary>
	public void SetMouseCapture( bool b )
	{
		if ( b )
		{
			MouseCapture = this;
			return;
		}

		if ( MouseCapture == this )
		{
			MouseCapture = null;
			return;
		}
	}

	/// <summary>Whether this panel is capturing the mouse cursor. See <see cref="SetMouseCapture"/>.</summary>
	[Hide]
	public bool HasMouseCapture => MouseCapture == this;

	//These are used by the input system as an optimization
	internal Vector2 WorldCursor;
	internal float WorldDistance = float.MaxValue;

	/// <summary>Transform a ray in 3D space to a position on the panel. This is used for world panel input.</summary>
	public virtual bool RayToLocalPosition( Ray ray, out Vector2 position, out float distance )
	{
		position = default;
		distance = default;

		return false;
	}
}
