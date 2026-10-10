using Rinten.UI;

namespace Editor;

/// <summary>Thin Widget host: one child Panel fills the dock slot, no nested Widget tree.</summary>
public class PanelDockHost : PanelWidget
{
	public Panel Content => Panel;

	public PanelDockHost( Widget parent = null ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;
	}

	public void SetContent( Panel panel ) => SetPanel( panel );
}
