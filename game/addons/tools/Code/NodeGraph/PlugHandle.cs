using Rinten.UI;

namespace Editor.NodeEditor;

//Three shapes and no drawing: a square and a circle are a box with a corner radius, and the arrow is the icon font, which every other arrow in the editor is as well.
/// <summary>The mark on a plug that a wire lands on.</summary>
public sealed class PlugHandle : Panel
{
	private readonly IconPanel _arrow;

	public PlugHandle()
	{
		AddClass( "handle" );

		_arrow = AddChild<IconPanel>( "arrow" );
		_arrow.Text = "play_arrow";
	}

	public void Apply( HandleShape shape, Color colour )
	{
		var isArrow = shape == HandleShape.Arrow;

		SetClass( "circle", shape == HandleShape.Circle );
		SetClass( "arrow", isArrow );

		_arrow.SetClass( "hidden", !isArrow );

		//An arrow is coloured by its glyph, other shapes by their fill; .arrow in the sheet drops it.
		if ( isArrow )
		{
			_arrow.Style.FontColor = colour;
			return;
		}

		Style.BackgroundColor = colour;
	}
}
