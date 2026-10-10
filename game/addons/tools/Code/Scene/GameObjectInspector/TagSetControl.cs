namespace Editor;

public sealed class TagChipPanel : Panel
{
	public TagChipPanel( string tag, Action<string> onRemove )
	{
		AddClass( "tag-chip" );
		AddChild<Rinten.UI.Label>( "text" ).Text = tag.ToLower();
		_onRemove = onRemove;
		_tag = tag;
	}

	readonly Action<string> _onRemove;
	readonly string _tag;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		_onRemove( _tag );
		e.StopPropagation();
	}
}
