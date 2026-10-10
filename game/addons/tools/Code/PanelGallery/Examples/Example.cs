using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The few bricks every hand-written example is built from, so the pages read alike.
static class Example
{
	/// <summary>Things stacked down the stage.</summary>
	public static Panel Column() => new Panel { Classes = "example" };

	/// <summary>Things side by side, wrapping when the stage is narrow.</summary>
	public static Panel Row() => new Panel { Classes = "example row" };

	/// <summary>A row inside an example.</summary>
	public static Panel Row( Panel into, string gap = "gap-2" )
		=> into.AddChild<Panel>( $"row {gap}" );

	/// <summary>A quiet line of text under something, saying what to look for or what just happened.</summary>
	public static Label Note( Panel into, string text )
	{
		var note = into.AddChild<Label>( "example-note" );
		note.Text = text;
		return note;
	}

	public static Label Text( Panel into, string text, string classes = null )
	{
		var label = into.AddChild<Label>( classes );
		label.Text = text;
		return label;
	}

	public static TextButtonPanel Button( Panel into, string text, string icon, Action pressed, string kind = null )
	{
		var button = into.AddChild<TextButtonPanel>();
		button.Text = text;
		button.Icon = icon;
		button.Pressed = pressed;

		if ( !string.IsNullOrEmpty( kind ) )
			button.AddClass( kind );

		return button;
	}

	/// <summary>A labelled box of a given size, for showing a class or a layout rather than a control.</summary>
	public static Panel Tile( Panel into, string text, string classes = null )
	{
		var tile = into.AddChild<Panel>( "tile" );

		if ( !string.IsNullOrEmpty( classes ) )
			tile.AddClass( classes );

		Text( tile, text );
		return tile;
	}
}
