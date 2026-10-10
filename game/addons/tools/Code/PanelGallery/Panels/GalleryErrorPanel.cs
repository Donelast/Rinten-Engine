using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

/// <summary>What stands where an example should, when the example threw.</summary>
public sealed class GalleryErrorPanel : Panel
{
	public GalleryErrorPanel( Exception error )
	{
		AddClass( "gallery-error" );

		var kind = AddChild<Label>( "kind" );
		kind.Text = error?.GetType().Name ?? "Error";

		var message = AddChild<Label>( "message" );
		message.Text = error?.Message ?? "";

		Tooltip = error?.ToString();
	}
}
