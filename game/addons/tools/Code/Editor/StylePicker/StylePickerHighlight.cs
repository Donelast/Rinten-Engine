using Rinten.Internal;
using Rinten.UI;

namespace Editor;

//Highlight box while the style picker is active.
internal sealed class StylePickerHighlight
{
	readonly Panel box;
	EditorOverlay layer;

	public StylePickerHighlight()
	{
		box = new Panel();
		box.AddClass( StylePickerTool.FurnitureClass );
		box.AddClass( "style-picker-highlight" );
	}

	public void PointAt( IPanel target, EditorOverlay onto, Vector2 offset = default )
	{
		if ( onto is null || !target.IsValid() )
		{
			Hide();
			return;
		}

		if ( !ReferenceEquals( layer, onto ) )
		{
			layer = onto;
			layer.AddChild( box );
		}

		var rect = target is Panel panel ? panel.GetScreenPixelRect() : target.OuterRect;

		box.Style.Left = rect.Left + offset.x;
		box.Style.Top = rect.Top + offset.y;
		box.Style.Width = rect.Width;
		box.Style.Height = rect.Height;
		box.SetClass( "hidden", false );
	}

	public void Hide() => box.SetClass( "hidden", true );

	public void Destroy() => box?.Delete( true );
}
