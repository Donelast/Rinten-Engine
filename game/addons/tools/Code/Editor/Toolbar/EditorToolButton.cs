using Rinten.UI;

namespace Editor;

//What it looks like is not here: the editor's own sheet dresses every .toolbarbutton in the window, so this only says which of them it is right now - lit, greyed out, and which letter of the icon font is on it.
/// <summary>The icon a toolbar command is pressed by.</summary>
public sealed class EditorToolButton : Panel
{
	private readonly IToolbarCommand command;
	private readonly IconPanel icon;

	private string shownIcon;
	private string shownToolTip;

	public EditorToolButton( IToolbarCommand command )
	{
		this.command = command;

		AddClass( "toolbarbutton" );

		icon = AddChild<IconPanel>( "icon" );

		Sync();
	}

	public override void Tick()
	{
		base.Tick();

		Sync();
	}

	//Says what the command says, writing only what changed.
	private void Sync()
	{
		if ( command is null ) return;

		var name = command.Icon;

		if ( name != shownIcon )
		{
			shownIcon = name;
			ShowIcon( name );
		}

		var tip = command.ToolTip;

		if ( tip != shownToolTip )
		{
			shownToolTip = tip;
			Tooltip = tip ?? string.Empty;
		}

		SetClass( "active", command.IsActive );
		SetClass( "disabled", !command.IsEnabled );
	}

	//A letter of the icon font
	private void ShowIcon( string name )
	{
		icon.Text = name ?? string.Empty;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( command is null || !command.IsEnabled ) return;

		command.Execute();

		e.StopPropagation();
	}
}
