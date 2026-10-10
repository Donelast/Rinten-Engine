namespace Editor.Gallery;

//WidgetPanel only borrows the widget it shows; this one owns it, because the gallery made it and nobody else will destroy it.
/// <summary>A widget example standing in a panel page, until it is rewritten or removed.</summary>
public sealed class LegacyWidgetHost : WidgetPanel
{
	public LegacyWidgetHost( Widget widget )
	{
		AddClass( "legacy-widget-host" );

		Widget = widget;
	}

	public override void OnDeleted()
	{
		var owned = Widget;

		Widget = null;

		base.OnDeleted();

		if ( owned.IsValid() )
			owned.Destroy();
	}
}
