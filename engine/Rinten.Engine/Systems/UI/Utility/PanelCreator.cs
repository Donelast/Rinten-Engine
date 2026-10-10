
namespace Rinten.UI.Construct;

/// <summary>Used for <see cref="Panel.Add"/> for quick panel creation with certain settings. Other panels types are added via extension methods.</summary>
public ref struct PanelCreator
{
	/// <summary>The panel to add children to.</summary>
	public Panel panel;

	internal PanelCreator( Panel panel )
	{
		this.panel = panel;
	}

	/// <summary>Add a new blank panel as a child.</summary>
	public Panel Panel()
	{
		return panel.AddChild<Panel>();
	}

	/// <summary>Add a new blank panel with given CSS classes as a child.</summary>
	public Panel Panel( string classname )
	{
		var control = panel.AddChild<Panel>();
		control.AddClass( classname );
		return control;
	}
}


