using Rinten.UI;
using System.Reflection;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The theme itself: every color it names, and the icon face the editor writes with.
static class ThemeExamples
{
	const string Page = "Theme";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Palette" ), Icon( "palette" )]
	[Description( "Every color on Editor.Theme, read by reflection - so a color added to theme.json is a swatch here without anyone writing one." )]
	static Panel Palette()
	{
		var example = Example.Row();

		var fields = typeof( Theme )
			.GetFields( BindingFlags.Public | BindingFlags.Static )
			.Where( x => x.FieldType == typeof( Color ) )
			.OrderBy( x => x.Name );

		foreach ( var field in fields )
			Swatch( example, field.Name, (Color)field.GetValue( null ) );

		var properties = typeof( Theme )
			.GetProperties( BindingFlags.Public | BindingFlags.Static )
			.Where( x => x.PropertyType == typeof( Color ) && x.GetIndexParameters().Length == 0 )
			.OrderBy( x => x.Name );

		foreach ( var property in properties )
			Swatch( example, property.Name, (Color)property.GetValue( null ) );

		return example;
	}

	static void Swatch( Panel into, string name, Color color )
	{
		var swatch = into.AddChild<Panel>( "swatch" );
		swatch.Style.BackgroundColor = color;
		swatch.Tooltip = $"{name} = {color.Hex}";

		Example.Text( swatch, name, "name" );
		//Without the hash: a label whose text starts with # takes it for a localisation token.
		Example.Text( swatch, color.Hex.TrimStart( '#' ), "value" );
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Icons" ), Icon( "emoji_symbols" )]
	[Description( "A handful of the material icons the editor uses, drawn by .material-icon." )]
	static Panel Icons()
	{
		var example = Example.Column();

		var grid = example.AddChild<Panel>( "icon-grid" );

		foreach ( var name in IconNames )
		{
			var cell = grid.AddChild<Panel>( "icon-cell" );
			cell.Tooltip = name;

			var icon = cell.AddChild<Label>( "material-icon" );
			icon.Text = name;

			Example.Text( cell, name, "name" );
		}

		return example;
	}

	static readonly string[] IconNames =
	[
		"home", "settings", "search", "add", "close", "delete", "edit", "save", "folder", "folder_open",
		"description", "image", "movie", "music_note", "terminal", "bug_report", "build", "tune", "palette", "brush",
		"view_in_ar", "account_tree", "device_hub", "grid_view", "grid_on", "list", "view_list", "dashboard", "widgets", "layers",
		"near_me", "open_with", "360", "zoom_out_map", "visibility", "visibility_off", "lock", "lock_open", "link", "link_off",
		"play_arrow", "pause", "stop", "skip_next", "skip_previous", "fast_forward", "fast_rewind", "loop", "shuffle", "replay",
		"check", "check_box", "radio_button_checked", "warning", "error", "info", "help", "star", "favorite", "bolt",
	];
}
