using Rinten.UI;

namespace Editor;

partial class BoolControlWidget
{
	private WidgetIconPanel glyphPanel;

	//Tick box inset 2px as its own panel; resizing the surface instead made checkboxes flicker
	private Panel plate;

	private protected override void OnSurfaceCreated()
	{
		base.OnSurfaceCreated();


		EnsureGlyphPanel();
		PaintBackground = false;
	}

	public override void SyncSurfaceContent()
	{

		EnsureGlyphPanel();
		SyncPanelBool();
	}

	private void EnsureGlyphPanel()
	{
		if ( glyphPanel is { IsValid: true } ) return;

		plate = new Panel();
		plate.AddClass( "widget-check" );
		Surface.AddChild( plate );

		glyphPanel = new WidgetIconPanel();
		plate.AddChild( glyphPanel );
	}

	private void SyncPanelBool()
	{
		if ( SerializedProperty is null ) return;
		if ( glyphPanel is null || !glyphPanel.IsValid() || Surface is null ) return;

		var alpha = IsControlDisabled ? 0.5f : 1.0f;

		var hovered = IsControlHovered && !ReadOnly;
		var pressed = IsPressed;

		if ( Icon is not null && !SerializedProperty.IsMultipleDifferentValues && !SerializedProperty.As.Bool )
		{
			plate.Style.BackgroundColor = Color.Transparent;
			glyphPanel.SetGlyph( Icon, Tint.WithAlpha( 0.3f * alpha ) );
		}
		else if ( SerializedProperty.IsMultipleDifferentValues )
		{
			plate.Style.BackgroundColor = (hovered ? Theme.VeilHover : Theme.Sunken).WithAlphaMultiplied( alpha );
			glyphPanel.SetGlyph( "remove", Tint.WithAlphaMultiplied( alpha ) );
		}
		else if ( SerializedProperty.As.Bool )
		{
			plate.Style.BackgroundColor = Tint.WithAlpha( 0.22f * alpha );
			glyphPanel.SetGlyph( Icon ?? "done", Tint.WithAlphaMultiplied( alpha ) );
		}
		else
		{
			plate.Style.BackgroundColor = (hovered ? Theme.VeilHover : Theme.Sunken).WithAlphaMultiplied( alpha );
			glyphPanel.SetGlyph( string.Empty, Color.Transparent );
		}

		plate.SetClass( "hovered", hovered );

		if ( hovered )
		{
			plate.Style.BorderColor = (pressed ? Theme.EdgeLit : Theme.Edge).WithAlphaMultiplied( alpha );
		}

		Surface.SetClass( "disabled", IsControlDisabled );
	}
}
