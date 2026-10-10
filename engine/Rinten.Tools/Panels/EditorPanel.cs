using Rinten.UI;

namespace Editor;

/// <summary>A panel that lives in the editor's window rather than in a game's interface.</summary>
public class EditorPanel : Panel
{
	public EditorPanel()
	{
		EditorShortcuts.Register( this );
		EditorEvent.Register( this );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		EditorShortcuts.Unregister( this );
		EditorEvent.Unregister( this );
	}

	/// <summary>The widget this panel is drawn inside of, which is the one that knows where the window put it.</summary>
	public Widget HostWidget
	{
		get
		{
			for ( Panel panel = this; panel is not null; panel = panel.Parent )
			{
				if ( panel is WidgetSurface surface ) return surface.Widget;
			}

			return null;
		}
	}

	/// <summary>The top left of this panel on the desktop, in editor pixels.</summary>
	public Vector2 ScreenPosition
	{
		get
		{
			var host = HostWidget;
			if ( !host.IsValid() || host.Surface is null ) return Box.Rect.Position;

			return host.ScreenPosition + (Box.Rect.Position - host.Surface.Box.Rect.Position) * ScaleFromScreen;
		}
	}

	/// <summary>How big this panel is in editor pixels, which is what a camera is sized against.</summary>
	public Vector2 ScreenSize => Box.Rect.Size * ScaleFromScreen;

	/// <summary>Where this panel is on the desktop, as a rectangle.</summary>
	public Rect ScreenRect => new( ScreenPosition, ScreenSize );

	/// <summary>Whether the pointer is over this panel.</summary>
	public bool IsUnderMouse => HasHovered;

	/// <summary>Whether the window this panel is in is the one the desktop is pointing at.</summary>
	public bool IsActiveWindow => HostWidget is { } host && host.IsValid() && host.IsActiveWindow;

	/// <summary>A point on the desktop, said in this panel's own pixels.</summary>
	public Vector2 FromScreen( Vector2 screenPosition ) => screenPosition - ScreenPosition;
}
