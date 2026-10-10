using Rinten.UI;

namespace Editor;

public partial class EmbeddedResourceControlWidget
{
	private Panel thumbChrome;
	private Rinten.UI.Image thumbImage;
	private Rinten.UI.Label displayLabel;
	private WidgetIconPanel expandPanel;

	private Texture cachedThumbTexture;
	private string cachedThumbKey;

	public override void SyncSurfaceContent()
	{
		base.SyncSurfaceContent();


		EnsureDropdownPanels();
		SyncEmbeddedDisplay();
	}

	private void EnsureDropdownPanels()
	{
		if ( displayLabel is { IsValid: true } ) return;

		ContentPanel.SetClass( "hidden", false );
		ContentPanel.AddClass( "widget-embedded-resource" );

		thumbChrome = new Panel();
		thumbChrome.AddClass( "widget-resource-thumb" );
		ContentPanel.AddChild( thumbChrome );

		thumbImage = new Rinten.UI.Image();
		thumbChrome.AddChild( thumbImage );

		displayLabel = new Rinten.UI.Label { Tokenize = false };
		displayLabel.AddClass( "widget-dropdown-text" );
		ContentPanel.AddChild( displayLabel );

		expandPanel = new WidgetIconPanel();
		ContentPanel.AddChild( expandPanel );
	}

	private void SyncEmbeddedDisplay()
	{
		if ( displayLabel is null || !displayLabel.IsValid() ) return;

		var pickerName = DisplayInfo.ForType( SerializedProperty.PropertyType ).Name;
		if ( _assetType is not null ) pickerName = _assetType.FriendlyName;

		var text = $"Embedded {pickerName}";
		if ( SerializedProperty.IsMultipleDifferentValues )
			text = "Multiple Values";

		var color = IsControlHovered ? Theme.Ink : Theme.InkDim;
		if ( IsControlDisabled || !Resource.IsValid() ) color = color.WithAlpha( 0.5f );

		displayLabel.Text = text;
		displayLabel.Style.FontColor = SerializedProperty.IsMultipleDifferentValues
			? Theme.MultipleValues.WithAlpha( IsControlDisabled ? 0.5f : 1f )
			: color;

		expandPanel.SetGlyph( "expand_more", color );

		var icon = _assetType?.Icon64;
		if ( icon?.picture is { IsValid: true } )
		{
			var alpha = IsControlHovered ? 1f : 0.7f;
			var key = icon.picture.GetHashCode().ToString();
			if ( cachedThumbKey != key || cachedThumbTexture is null || !cachedThumbTexture.IsValid )
			{
				cachedThumbKey = key;
				cachedThumbTexture = icon.picture.ToTexture( mips: false );
			}

			thumbImage.Texture = cachedThumbTexture;
			thumbImage.SetClass( "hidden", false );
			thumbImage.Style.Opacity = alpha;
		}
		else
		{
			thumbImage.Texture = null;
			thumbImage.SetClass( "hidden", true );
		}
	}
}
