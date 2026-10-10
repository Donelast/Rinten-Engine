using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

//Menus are still popup windows of widgets, and an option in one closes it when clicked. Some of them must not - a list of sub-tracks you tick several of at once - so those are panels, on the bridge the rest of the editor uses.
/// <summary>A panel put inside a widget menu.</summary>
public sealed class MenuPanelItem : PanelWidget
{
	public MenuPanelItem( Panel panel, float height = 24f, float minimumWidth = 200f )
	{
		FixedHeight = height;
		MinimumWidth = minimumWidth;

		SetPanel( panel );
	}
}
