using Rinten.UI;
using System;

namespace Editor;

/// <summary>A row of buttons across the top of a panel-native dock.</summary>
public class PanelToolbar : Panel
{
	public PanelToolbar()
	{
		AddClass( "panel-toolbar" );

		EditorStyles.Apply( this, "PanelToolbar.cs.scss" );
	}

	/// <summary>An icon that does something when pressed.</summary>
	public PanelToolbarButton AddIcon( string icon, string tooltip, Action pressed )
	{
		var button = AddChild<PanelToolbarButton>();

		button.SetIcon( icon );
		button.Tooltip = tooltip;
		button.Pressed = pressed;

		return button;
	}

	/// <summary>An icon that stays down while whatever it turns on is on.</summary>
	public PanelToolbarButton AddToggle( string icon, string tooltip, bool active, Action<bool> toggled )
	{
		var button = AddChild<PanelToolbarButton>();

		button.SetIcon( icon );
		button.Tooltip = tooltip;
		button.IsToggle = true;
		button.IsActive = active;
		button.Toggled = toggled;

		return button;
	}

	/// <summary>A word that stays down while whatever it turns on is on.</summary>
	public PanelToolbarButton AddTextToggle( string text, string tooltip, bool active, Action<bool> toggled )
	{
		var button = AddChild<PanelToolbarButton>();

		button.SetText( text );
		button.Tooltip = tooltip;
		button.IsToggle = true;
		button.IsActive = active;
		button.Toggled = toggled;

		return button;
	}

	/// <summary>A word rather than a glyph.</summary>
	public PanelToolbarButton AddText( string text, string tooltip, Action pressed )
	{
		var button = AddChild<PanelToolbarButton>();

		button.SetText( text );
		button.Tooltip = tooltip;
		button.Pressed = pressed;

		return button;
	}

	//Every list dock has one and they were all built by hand, down to the same magnifier glyph. The bar owns it because the bar is what has room to give.
	/// <summary>A field for narrowing whatever the dock is listing, taking the room the buttons leave.</summary>
	public TextField AddFilter( string placeholder, Action<string> changed )
	{
		var field = AddChild<TextField>( "filter" );

		field.Placeholder = placeholder;
		field.Icon = "search";
		field.ValueChanged = changed;

		return field;
	}

	/// <summary>A hairline between two groups of buttons.</summary>
	public Panel AddSeparator() => AddChild<Panel>( "separator" );

	/// <summary>Empty room that pushes whatever follows it to the far end.</summary>
	public Panel AddStretch() => AddChild<Panel>( "stretch" );
}

/// <summary>One button in a <see cref="PanelToolbar"/>.</summary>
public class PanelToolbarButton : Panel
{
	private Rinten.UI.Label content;

	/// <summary>Run when the button is pressed, where it is not a toggle.</summary>
	public Action Pressed { get; set; }

	/// <summary>Run with the new state when a toggle is pressed.</summary>
	public Action<bool> Toggled { get; set; }

	/// <summary>Whether pressing it flips it rather than firing once.</summary>
	public bool IsToggle { get; set; }

	/// <summary>Whether it is lit.</summary>
	public bool IsActive
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			SetClass( "active", value );
		}
	}

	//Here rather than at every call site: the class that dims a button is design.scss's is-disabled , and every dock that had a button to grey out was writing that class itself and hoping the two stayed in step.
	/// <summary>Whether the button can be pressed at all. A disabled one is dimmed and takes no clicks.</summary>
	public bool Enabled
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			SetClass( "is-disabled", !value );
		}
	} = true;

	public PanelToolbarButton()
	{
		AddClass( "panel-toolbar-button" );
	}

	/// <summary>Puts a material icon on the button.</summary>
	public void SetIcon( string icon )
	{
		Content( "icon" ).Text = icon;
	}

	/// <summary>Puts a word on the button.</summary>
	public void SetText( string text )
	{
		Content( "text" ).Text = text;
	}

	//The one label a button has, in whichever of its two kinds is wanted. The class is set every time rather than at birth: a button
	//made as an icon and then given a word keeps the icon font otherwise, and a word in the icon font is a row of ligature attempts.
	private Rinten.UI.Label Content( string classname )
	{
		if ( content is null )
		{
			content = AddChild<Rinten.UI.Label>();
			content.Tokenize = false;
		}

		content.SetClass( "icon", classname == "icon" );
		content.SetClass( "text", classname == "text" );

		return content;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		//The sheet already stops the pointer at a disabled button; this is for a caller that styles
		//it some other way, so the rule lives with the button rather than only in the stylesheet.
		if ( !Enabled ) return;

		if ( IsToggle )
		{
			IsActive = !IsActive;
			Toggled?.Invoke( IsActive );

			return;
		}

		Pressed?.Invoke();
	}
}
