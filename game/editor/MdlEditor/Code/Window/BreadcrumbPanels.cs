using Rinten.UI;
using System;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Breadcrumb nav arrow (back/forward/up/home); enabled state re-checked from history each tick
[StyleSheet( "BreadcrumbPanels.cs.scss" )]
internal sealed class NavButtonPanel : Panel
{
	private readonly Action _onClick;
	private readonly Func<bool> _enabled;

	private bool _isEnabled = true;

	internal NavButtonPanel( string icon, string tooltip, Action onClick, Func<bool> enabled )
	{
		_onClick = onClick;
		_enabled = enabled;

		AddClass( "nav-button" );
		Tooltip = tooltip;

		var glyph = AddChild<UILabel>( "material-icon" );
		glyph.Text = icon;

		Refresh();
	}

	public override void Tick()
	{
		base.Tick();

		Refresh();
	}

	//Asks again whether there is anywhere to go, and goes out if not.
	internal void Refresh()
	{
		var enabled = _enabled?.Invoke() ?? true;
		if ( enabled == _isEnabled )
			return;

		_isEnabled = enabled;
		SetClass( "disabled", !enabled );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		//A disabled arrow still takes the pointer, so that its tooltip is there while there is
		//nowhere to go - which means the guard has to be here rather than left to the sheet.
		if ( !_isEnabled )
			return;

		_onClick?.Invoke();
		e.StopPropagation();
	}
}

//One breadcrumb step: glyph and label, current or clickable; shared with the secondary header
[StyleSheet( "BreadcrumbPanels.cs.scss" )]
internal sealed class CrumbButtonPanel : Panel
{
	private readonly UILabel _icon;
	private readonly UILabel _title;
	private readonly Action _onClick;

	//Whether this step is the one being shown.
	internal bool IsCurrent { get; }

	//A step with no handler is the end of the trail unless told otherwise - the secondary header has two steps and neither of them goes anywhere.
	internal CrumbButtonPanel( string title, string icon, Color? tint, Action onClick, bool? isCurrent = null )
	{
		_onClick = onClick;
		IsCurrent = isCurrent ?? onClick is null;

		AddClass( "crumb-button" );
		SetClass( "link", onClick is not null );
		SetClass( "current", IsCurrent );

		_icon = AddChild<UILabel>( "material-icon icon" );

		_title = AddChild<UILabel>( "title" );

		//A node is named by whoever made it, and a name that opens with a hash is a language token to a label that is left tokenizing.
		_title.Tokenize = false;

		SetContent( title, icon, tint );
	}

	//Steps back to whatever this crumb stands for. A step with no handler stays silent, which is
	//what makes the last crumb and the secondary header's two labels inert without a class for it.
	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( _onClick is null ) return;

		_onClick();
		e.StopPropagation();
	}

	/// <summary>What this step says, and the colour of the node it stands for.</summary>
	public void SetContent( string title, string icon, Color? tint = null )
	{
		_title.Text = title;

		_icon.Text = icon;
		_icon.SetClass( "hidden", string.IsNullOrEmpty( icon ) );

		//The owning node's colour, which only the caller knows; null hands the step back to the sheet, where being current or hovered decides.
		Style.FontColor = tint;
	}
}
