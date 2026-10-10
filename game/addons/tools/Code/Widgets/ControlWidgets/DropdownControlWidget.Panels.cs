using Rinten.UI;

namespace Editor;

public partial class DropdownControlWidget<T>
{
	private Rinten.UI.Label displayLabel;
	private Rinten.UI.Label expandPanel;

	public override void SyncSurfaceContent()
	{
		base.SyncSurfaceContent();


		EnsureDropdownPanels();
		SyncDropdownDisplay();
	}

	private void EnsureDropdownPanels()
	{
		if ( displayLabel is { IsValid: true } ) return;

		ContentPanel.SetClass( "hidden", false );
		ContentPanel.AddClass( "widget-dropdown" );

		displayLabel = new Rinten.UI.Label { Tokenize = false };
		displayLabel.AddClass( "widget-dropdown-text" );

		expandPanel = new Rinten.UI.Label { Text = "Arrow_Drop_Down", Tokenize = false };
		expandPanel.AddClass( "widget-icon" );

		ContentPanel.AddChild( displayLabel );
		ContentPanel.AddChild( expandPanel );
	}

	private void SyncDropdownDisplay()
	{
		if ( displayLabel is null || !displayLabel.IsValid() || expandPanel is null ) return;

		var color = Theme.TextControl;
		if ( IsControlDisabled ) color = color.WithAlpha( 0.5f );

		expandPanel.Style.FontColor = color;

		if ( SerializedProperty is null ) return;

		if ( SerializedProperty.IsMultipleDifferentValues )
		{
			displayLabel.Text = "Multiple Values";
			displayLabel.Style.FontColor = Theme.MultipleValues.WithAlpha( IsControlDisabled ? 0.5f : 1f );
		}
		else
		{
			displayLabel.Style.FontColor = color;
			displayLabel.Text = GetDisplayText();
		}
	}
}
