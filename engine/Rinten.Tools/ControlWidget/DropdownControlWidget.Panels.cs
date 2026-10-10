using Rinten.UI;

namespace Editor;

partial class DropdownControlWidget
{
	private Rinten.UI.Label displayLabel;
	private WidgetIconPanel expandPanel;

	public override void SyncSurfaceContent()
	{
		base.SyncSurfaceContent();


		EnsureDropdownPanels();
		SyncDropdownDisplay();
	}

	private void EnsureDropdownPanels()
	{
		if ( displayLabel is { IsValid: true } ) return;

		Surface.SetClass( "hidden", false );
		Surface.AddClass( "widget-dropdown" );

		displayLabel = new Rinten.UI.Label { Tokenize = false };
		displayLabel.AddClass( "widget-dropdown-text" );

		expandPanel = new WidgetIconPanel();

		Surface.AddChild( displayLabel );
		Surface.AddChild( expandPanel );
	}

	private void SyncDropdownDisplay()
	{
		if ( displayLabel is null || !displayLabel.IsValid() || expandPanel is null ) return;

		var color = IsControlHovered ? Theme.Ink : Theme.InkDim;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		expandPanel.SetGlyph( "expand_more", color );

		if ( SerializedProperty is null ) return;

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			displayLabel.Text = "Multiple Values";
			displayLabel.Style.FontColor = Theme.MultipleValues.WithAlpha( IsControlDisabled ? 0.5f : 1f );
		}
		else
		{
			displayLabel.Style.FontColor = color;
			displayLabel.Text = GetDropdownDisplayText();
		}
	}
}
