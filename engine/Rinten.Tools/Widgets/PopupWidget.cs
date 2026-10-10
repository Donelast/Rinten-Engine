using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>A popup that sits on the window overlay and deletes itself when it closes.</summary>
public class PopupWidget : Widget
{
	public bool PreventDestruction = false;

	public Action OnLostFocus { get; set; }

	//What opened this, so the overlay can still be found after it is taken off its parent.
	internal Widget Owner { get; }

	private EditorOverlay host;

	private static readonly List<PopupWidget> open = new();

	public PopupWidget( Widget widget ) : base( widget )
	{
		Owner = widget;
		WindowFlags = WindowFlags.Popup;
		DeleteOnClose = true;
		MouseTracking = true;
		Visible = false;
	}

	protected override void OnVisibilityChanged( bool visible )
	{
		base.OnVisibilityChanged( visible );

		if ( !visible && !PreventDestruction )
		{
			OnLostFocus?.Invoke();
			Destroy();
		}
	}

	public override void OnDestroyed()
	{
		open.Remove( this );
		host = null;

		base.OnDestroyed();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;

		Paint.ClearPen();
		Paint.SetBrush( Theme.Glass );
		Paint.DrawRect( LocalRect, Theme.ControlRadius );

		Paint.ClearBrush();
		Paint.SetPen( Theme.Edge, 1 );
		Paint.DrawRect( LocalRect.Shrink( 0.5f ), Theme.ControlRadius );
	}

	public override void Show()
	{
		Present( Position );
	}

	public void OpenAtCursor( bool animate = true, Vector2? offset = null )
	{
		var offsetActual = Vector2.Zero;
		if ( offset.HasValue ) offsetActual = offset.Value;

		var pos = Application.CursorPosition + offsetActual;

		OpenAt( pos, animate );
	}

	public void OpenAt( Vector2 position, bool animate = true, Vector2? animateOffset = null )
	{
		if ( animate )
			WindowOpacity = 0;

		Present( position );

		if ( animate )
		{
			var offset = animateOffset ?? new Vector2( 0, -8 );
			Animate.Add( this, 0.2f, Position.y + offset.y, Position.y, y => { Position = Position.WithY( y ); OnMoved(); }, "ease-out" );
			Animate.Add( this, 0.2f, Position.x + offset.x, Position.x, x => { Position = Position.WithX( x ); OnMoved(); }, "ease-out" );
			Animate.Add( this, 0.1f, 0, 1, y => WindowOpacity = y, "linear" );
		}
	}

	/// <summary>Open this many pixels below the cursor.</summary>
	public void OpenBelowCursor( float distance, float centering = 0.5f )
	{
		AttachToOverlay();
		AdjustSize();
		Present( Application.CursorPosition - Size * new Vector2( centering, 0.0f ) + new Vector2( 0, distance ) );
	}

	//Closes every popup the press did not land in, as clicking away from one does.
	internal static void PressedOn( Widget widget )
	{
		open.RemoveAll( x => !x.IsValid );

		if ( open.Count == 0 ) return;

		for ( var w = widget; w is not null; w = w.Parent )
		{
			if ( w is PopupWidget ) return;
		}

		foreach ( var popup in open.ToArray() )
		{
			if ( popup.PreventDestruction ) continue;

			popup.OnLostFocus?.Invoke();
			popup.Destroy();
		}
	}

	private void Present( Vector2 position )
	{
		AttachToOverlay();
		Position = position;
		Visible = true;
		PlaceOnOverlay();

		if ( !open.Contains( this ) )
			open.Add( this );
	}

	private void AttachToOverlay()
	{
		var layer = HostLayer();

		if ( layer is null || Surface is null )
		{
			//Said rather than swallowed. Without a layer this widget is marked visible and put on nothing,
			//which is a dialog that does not open and does not complain - the hardest kind of thing to report.
			Log.Warning( $"{GetType().Name}: no window layer to open on, so it will not be seen" );
			return;
		}

		SetPanelParent( null );

		if ( Surface.Parent is not null ) Surface.Parent = null;

		layer.AddChild( Surface );
		host = layer;
	}

	private void PlaceOnOverlay()
	{
		AdjustSize();

		if ( host is null ) return;

		var bounds = host.Bounds;

		if ( bounds.x > 1 && bounds.y > 1 )
			Position = host.Place( Position, Size );
	}

	//Overlay layer for this popup: owner's window, else the one under the pointer, created if needed
	private EditorOverlay HostLayer()
	{
		if ( EditorOverlay.Find( this ) is { } here ) return here;
		if ( EditorOverlay.For( Owner ) is { } from ) return from;

		//Nothing said who opened it. A popup opens at the cursor, so the window the cursor is in is the one it was meant for.
		if ( WidgetSurface.Hovered is { IsValid: true } hovered
			&& EditorOverlay.For( hovered ) is { } under )
			return under;

		//And last, whichever window can give us one at all - not the newest alone, which may be a window with no layer of its own.
		for ( var i = Window.All.Count - 1; i >= 0; i-- )
		{
			if ( EditorOverlay.For( Window.All[i]?.Shell?.Root ) is { } layer ) return layer;
		}

		return null;
	}
}
