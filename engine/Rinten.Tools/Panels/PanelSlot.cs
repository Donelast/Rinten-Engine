using Rinten.UI;

namespace Editor;

/// <summary>A panel somebody else built, standing in a razor layout.</summary>
public sealed class PanelSlot : Panel
{
	private Panel content;

	public PanelSlot()
	{
		AddClass( "panelslot" );
	}

	/// <summary>The panel standing here.</summary>
	public Panel Content
	{
		get => content;
		set
		{
			if ( content == value ) return;

			if ( content is { IsValid: true } ) content.Parent = null;

			content = value;

			if ( content is { IsValid: true } ) AddChild( content );
		}
	}
}
