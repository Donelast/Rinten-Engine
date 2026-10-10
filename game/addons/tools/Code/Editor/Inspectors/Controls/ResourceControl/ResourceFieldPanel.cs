namespace Editor.Inspectors;

//Clickable resource field: thumbnail, title, path, or placeholder.
sealed class ResourceFieldPanel : Rinten.UI.Panel
{
	readonly ResourceThumbPanel _thumb;
	readonly Rinten.UI.Panel _text;
	readonly Rinten.UI.Label _title;
	readonly Rinten.UI.Label _path;
	readonly Rinten.UI.Label _placeholder;

	public ResourceFieldPanel()
	{
		AddClass( "field" );

		_thumb = AddChild<ResourceThumbPanel>();
		_text = AddChild<Rinten.UI.Panel>( "text" );
		_title = _text.AddChild<Rinten.UI.Label>( "title" );
		_path = _text.AddChild<Rinten.UI.Label>( "path" );
		_placeholder = AddChild<Rinten.UI.Label>( "placeholder" );
		_placeholder.Tokenize = false;
		_title.Tokenize = false;
		_path.Tokenize = false;
	}

	public void Update( ResourceDisplayState state )
	{
		SetClass( "none", false );
		SetClass( "placeholder", state.Mode == ResourceFieldMode.Placeholder );
		SetClass( "multiple", state.Mode == ResourceFieldMode.Multiple );
		SetClass( "set", state.Mode == ResourceFieldMode.Set );
		SetClass( "missing", state.Mode == ResourceFieldMode.Missing );

		_thumb.Update( state );

		_title.SetClass( "multiple", state.Mode == ResourceFieldMode.Multiple );
		_title.SetClass( "missing", state.Mode == ResourceFieldMode.Missing );

		var showText = state.Mode is ResourceFieldMode.Multiple or ResourceFieldMode.Set or ResourceFieldMode.Missing;
		_text.SetClass( "hidden", !showText );
		_placeholder.SetClass( "hidden", state.Mode != ResourceFieldMode.Placeholder );

		if ( showText )
		{
			_title.Text = state.Title;
			_path.Text = state.Path;
		}
		else
		{
			_placeholder.Text = state.Placeholder;
		}
	}
}
