namespace Editor;

//Rename field that cancels on escape.
sealed class TreeRenameField : TextField
{
	public TreeRenameField()
	{
		ElementName = "textfield";
	}

	public Action OnCancel { get; set; }

	public override void OnButtonTyped( ButtonEvent e )
	{
		if ( e.Pressed && e.Button == "escape" )
		{
			e.StopPropagation = true;
			OnCancel?.Invoke();
			return;
		}

		base.OnButtonTyped( e );
	}
}
