using Rinten.UI;
using System;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//One kind a list can be filtered to: its key, title, icon and colour
internal sealed record TagChip( string Key, string Title, string Icon, Color Color );

//A chip row in the picker: left-press includes the kind, right-press excludes it
[StyleSheet( "TagChip.cs.scss" )]
sealed class TagChipOption : Panel
{
	/// <summary>Which kind this row stands for.</summary>
	public TagChip Chip { get; }

	/// <summary>Pressed with the left button: show only this kind.</summary>
	public Action Included { get; set; }

	/// <summary>Pressed with the right button: show everything but this kind.</summary>
	public Action Excluded { get; set; }

	/// <summary>Whether the filter is on this kind.</summary>
	public bool IsSelected
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			SetClass( "selected", value );
		}
	}

	/// <summary>Whether the filter is on everything but this kind.</summary>
	public bool IsExcluded
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			SetClass( "excluded", value );
		}
	}

	public TagChipOption( TagChip chip, int count = 0 )
	{
		Chip = chip;

		AddClass( "tag-option" );

		//The colour bar, where the kind has a colour of its own to show.
		if ( chip.Color.a > 0 )
			AddChild<Panel>( "swatch" ).Style.BackgroundColor = chip.Color;

		if ( !string.IsNullOrEmpty( chip.Icon ) )
		{
			var icon = AddChild<UILabel>( "material-icon icon" );
			icon.Text = chip.Icon;
			icon.Style.FontColor = chip.Color;
		}

		var title = AddChild<UILabel>( "title" );
		title.Text = chip.Title;

		//How many there are of this kind, once - a count that moves while the picker is open would be the list changing under the pointer.
		if ( count > 0 )
		{
			var found = AddChild<UILabel>( "count" );
			found.Text = $"{count:n0}";
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Left ) Included?.Invoke();
		else if ( e.MouseButton == MouseButtons.Right ) Excluded?.Invoke();
		else return;

		e.StopPropagation();
	}
}

//The same chip, small, standing beside the search field to say the filter is on. Clicking it takes the filter off again. Its icon
//is the whole of it where it has one; the name is the tooltip, so a row of these stays a row of glyphs rather than a sentence.
[StyleSheet( "TagChip.cs.scss" )]
sealed class TagChipEntry : Panel
{
	/// <summary>Which kind this chip stands for.</summary>
	public TagChip Chip { get; }

	/// <summary>Pressed: this kind is no longer part of the filter.</summary>
	public Action Removed { get; set; }

	public TagChipEntry( TagChip chip, bool excluded = false )
	{
		Chip = chip;

		AddClass( "tag-entry" );
		SetClass( "excluded", excluded );

		Tooltip = chip.Title;

		if ( !string.IsNullOrEmpty( chip.Icon ) )
		{
			var icon = AddChild<UILabel>( "material-icon icon" );
			icon.Text = chip.Icon;
			icon.Style.FontColor = chip.Color;
		}
		else
		{
			var title = AddChild<UILabel>( "title" );
			title.Text = chip.Title;
			title.Style.FontColor = chip.Color;
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		Removed?.Invoke();
		e.StopPropagation();
	}
}
