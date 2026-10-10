using Rinten.UI;
namespace Rinten;

public partial class PanelComponent
{
	public bool HasClass( string className ) => Panel.HasClass( className );

	public void RemoveClass( string className ) => Panel.RemoveClass( className );

	public void AddClass( string className ) => Panel.AddClass( className );

	public void BindClass( string className, Func<bool> func ) => Panel.BindClass( className, func );

	public void SetClass( string className, bool enabled ) => Panel.SetClass( className, enabled );
}
