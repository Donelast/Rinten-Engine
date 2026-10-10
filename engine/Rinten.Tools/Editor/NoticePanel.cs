namespace Editor;

/// <summary>The one panel surface behind every editor notification - the progress bar in the corner, the launcher, the startup bar. They used to each paint their own; having a single surface here is what makes them read as one system.</summary>
public static class NoticePanel
{
	/// <summary>The one surface. Every translucent thing in the editor - notifications, the startup bar, the launcher - fills with exactly this, so they are the same colour and the same weight of glass. Taken from the editor's own window colour so it follows the theme rather than drifting from it.</summary>
	public static Color Background => Theme.WindowBackground.WithAlpha( Alpha );

	/// <summary>How solid the glass is. The single knob for all of it.</summary>
	public const float Alpha = 0.72f;

	/// <summary>Hairline that separates the panel from whatever is behind it.</summary>
	public static Color Edge => Color.White.WithAlpha( 0.08f );

	public const float Radius = 8f;
	public const float ShadowOffset = 3f;

	/// <summary>Draws the surface and returns the rect to lay content out in.</summary>
	public static Rect Draw( Rect rect, Color accent = default, float radius = Radius, bool shadow = true,
		Color fill = default )
	{
		if ( fill.a <= 0f )
			fill = Background;

		var body = rect;

		if ( shadow )
		{
			body = rect.Shrink( 0, 0, ShadowOffset, ShadowOffset );

			Paint.ClearPen();
			Paint.SetBrush( Color.Black.WithAlpha( 0.2f ) );
			Paint.DrawRect( rect.Shrink( ShadowOffset, ShadowOffset, 0, 0 ), radius );
		}

		var hasAccent = accent.a > 0f;

		Paint.SetBrush( fill );
		Paint.SetPen( hasAccent ? accent : Edge, hasAccent ? 2f : 1f );
		Paint.DrawRect( body.Shrink( hasAccent ? 2f : 0.5f ), radius );

		return body;
	}
}
