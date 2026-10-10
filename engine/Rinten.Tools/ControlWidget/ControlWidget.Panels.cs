using Rinten.UI;

namespace Editor;

public partial class ControlWidget
{
	Panel fieldChrome;

	public override bool UsesCanvasDraw => PaintBackground && base.UsesCanvasDraw;

	public override void SyncSurfaceContent()
	{
		if ( !PaintBackground || ContentPanel is null )
			return;

		if ( fieldChrome is null )
		{
			fieldChrome = new Panel();
			fieldChrome.AddClass( "control-field" );
			ContentPanel.AddChild( fieldChrome );

			//Move the field background beneath the control's parts, which were added first
			ContentPanel.SetChildIndex( fieldChrome, 0 );
		}

		fieldChrome.SetClass( "disabled", IsControlDisabled );
		fieldChrome.SetClass( "hovered", IsControlHovered && !IsControlDisabled );
		fieldChrome.SetClass( "active", IsControlActive && !IsControlDisabled );
		fieldChrome.SetClass( "pressed", IsPressed && !IsControlDisabled );
		fieldChrome.SetClass( "button", IsControlButton );
		fieldChrome.SetClass( "drop-target", IsBeingDroppedOn );
	}
}
