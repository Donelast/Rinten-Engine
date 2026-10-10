using Rinten.UI;

namespace Editor;

partial class FloatControlWidget
{
	private Panel iconChrome;
	private WidgetIconPanel iconGlyph;
	private Rinten.UI.Label axisLabel;
	private Rinten.UI.Label unitLabel;

	public override void SyncSurfaceContent()
	{
		base.SyncSurfaceContent();


		PaintBackground = false;
		SyncFloatChrome();
	}

	private void SyncFloatChrome()
	{
		if ( Surface is null ) return;

		var h = Size.y;
		var hovered = IsUnderMouse && Enabled;
		var alpha = IsControlDisabled ? 0.5f : 1.0f;

		if ( Icon is not null || Label is not null )
		{
			EnsureIconChrome();

			//The painted square: the widget's height, two pixels in on every side. Said each sync rather than once when it was made - it was made at the
			//size every widget starts at, before a layout had said anything, and was a full height wide and tall besides, so it hung out of the field.
			var side = Length.Pixels( System.MathF.Max( h - 4.0f, 0.0f ) );
			iconChrome.Style.Width = side;
			iconChrome.Style.Height = side;

			var boxColor = HighlightColor.Darken( hovered ? 0.7f : 0.8f ).Desaturate( 0.8f ).WithAlphaMultiplied( alpha );
			iconChrome.Style.BackgroundColor = boxColor;

			var glyphColor = HighlightColor.Darken( hovered ? 0.0f : 0.1f ).Desaturate( hovered ? 0.0f : 0.2f ).WithAlphaMultiplied( alpha );

			if ( string.IsNullOrEmpty( Label ) )
			{
				iconGlyph.SetGlyph( Icon, glyphColor );
				axisLabel.SetClass( "hidden", true );
			}
			else
			{
				iconGlyph.SetGlyph( string.Empty, Color.Transparent );
				axisLabel.SetClass( "hidden", false );
				axisLabel.Text = Label;
				axisLabel.Style.FontColor = glyphColor;
			}
		}

		if ( !string.IsNullOrEmpty( Unit ) )
		{
			EnsureUnitLabel();
			unitLabel.Text = Unit;
			unitLabel.Style.FontColor = Theme.InkDim.WithAlphaMultiplied( alpha );
		}
		else if ( unitLabel is { IsValid: true } )
		{
			unitLabel.SetClass( "hidden", true );
		}
	}

	private void EnsureIconChrome()
	{
		if ( iconChrome is { IsValid: true } ) return;

		iconChrome = new Panel();
		iconChrome.AddClass( "widget-float-icon" );
		Surface.AddChild( iconChrome );

		iconGlyph = new WidgetIconPanel();
		iconChrome.AddChild( iconGlyph );

		axisLabel = new Rinten.UI.Label { Tokenize = false };
		axisLabel.AddClass( "widget-float-axis" );
		iconChrome.AddChild( axisLabel );
	}

	private void EnsureUnitLabel()
	{
		if ( unitLabel is { IsValid: true } ) return;

		unitLabel = new Rinten.UI.Label { Tokenize = false };
		unitLabel.AddClass( "widget-float-unit" );
		Surface.AddChild( unitLabel );
	}
}
