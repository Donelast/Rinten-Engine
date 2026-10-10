using Rinten.UI;
using UILabel = Rinten.UI.Label;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>A band round the whole view saying why the graph cannot be saved.</summary>
public sealed class WarningFrame : Panel
{
	private readonly UILabel _label;

	public Color Color { get; set; } = Color.Parse( "#FA9131" )!.Value;

	public string? Text
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			Apply();
		}
	}

	public WarningFrame()
	{
		//It says something and takes nothing: the graph underneath is still one you can move about and read, it just cannot be saved.
		AddClass( "warning-frame" );

		_label = AddChild<UILabel>( "message" );
		_label.Tokenize = false;

		Apply();
	}

	private void Apply()
	{
		var shown = !string.IsNullOrEmpty( Text );

		SetClass( "hidden", !shown );

		if ( !shown ) return;

		Style.BorderColor = Color;
		_label.Style.BackgroundColor = Color;
		_label.Text = Text;
	}
}
