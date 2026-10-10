using Rinten.UI;
using System;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Icon button with a small corner glyph meaning it opens something; built on .toolbarbutton
[StyleSheet( "CornerIconButton.cs.scss" )]
sealed class CornerIconButton : Panel
{
	readonly UILabel _icon;
	readonly UILabel _corner;

	/// <summary>What pressing it does.</summary>
	public Action Pressed { get; set; }

	/// <summary>The glyph in the middle, by name.</summary>
	public string Icon
	{
		get => _icon.Text;
		set => _icon.Text = value ?? "";
	}

	/// <summary>The small glyph in the corner. Nothing shows where there is none.</summary>
	public string CornerIcon
	{
		get => _corner.Text;
		set
		{
			_corner.Text = value ?? "";
			_corner.SetClass( "hidden", string.IsNullOrWhiteSpace( value ) );
		}
	}

	/// <summary>What colour the corner glyph is drawn in - the caller's choice, so it is set here.</summary>
	public Color CornerColor
	{
		get;
		set
		{
			field = value;

			_corner.Style.FontColor = value;
		}
	}

	/// <summary>Whether the button is lit, because whatever it opens has something on.</summary>
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

	public CornerIconButton( string icon = null, Action pressed = null )
	{
		AddClass( "toolbarbutton corner-icon-button" );

		_icon = AddChild<UILabel>( "material-icon icon" );
		_corner = AddChild<UILabel>( "material-icon corner" );

		Icon = icon;

		//The defaults the widget had. Set rather than declared, because the corner is a child and a property initialiser runs before it exists.
		CornerIcon = "arrow_drop_down";
		CornerColor = Color.White;

		Pressed = pressed;
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Pressed?.Invoke();

		e.StopPropagation();
	}
}
