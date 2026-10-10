using Rinten.UI;

namespace Editor;

/// <summary>Material Icons glyph on a panel, like Razor <c>&lt;i&gt;</c>.</summary>
public sealed class WidgetIconPanel : Rinten.UI.Label
{
	public WidgetIconPanel()
	{
		AddClass( "widget-icon" );
		AddClass( "widget-icon-fill" );
		Tokenize = false;
	}

	public void SetGlyph( string glyph, Color tint )
	{
		Text = glyph ?? string.Empty;
		Style.FontColor = tint;
	}
}
