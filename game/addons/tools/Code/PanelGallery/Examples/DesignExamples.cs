using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The classes in design.scss that every panel in the editor wears: surfaces, edges, spacing, rows.
static class DesignExamples
{
	const string Page = "Design";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Surfaces" ), Icon( "layers" )]
	[Description( "One box per surface class. The names are the ones design.scss uses; a new surface is a new tile here." )]
	static Panel Surfaces()
	{
		var example = Example.Row();

		foreach ( var name in new[] { "sunken", "well", "card", "raised", "pressable", "btn" } )
			Example.Tile( example, "." + name, name );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Edges" ), Icon( "horizontal_rule" )]
	[Description( "A .hairline is a line of $edge; a .seam is the gap between panels with the layer below showing through." )]
	static Panel Edges()
	{
		var example = Example.Column();

		Example.Text( example, "hairline", "micro" );
		example.AddChild<Panel>( "hairline" ).Style.Height = 1f;

		Example.Text( example, "seam", "micro" );
		example.AddChild<Panel>( "seam" ).Style.Height = 4f;

		Example.Text( example, "seam-h", "micro" );

		var row = Example.Row( example, "gap-1" );
		Example.Tile( row, "left", "well" );
		row.AddChild<Panel>( "seam-h" ).Style.Width = 4f;
		Example.Tile( row, "right", "well" );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Spacing" ), Icon( "space_bar" )]
	[Description( ".gap-1 to .gap-4 on a .row or .column, so no panel invents its own gap." )]
	static Panel Spacing()
	{
		var example = Example.Column();

		for ( var i = 1; i <= 4; i++ )
		{
			Example.Text( example, $"gap-{i}", "micro" );

			var row = Example.Row( example, $"gap-{i}" );

			for ( var j = 0; j < 4; j++ )
				Example.Tile( row, "", "sunken" ).Style.Width = 48f;
		}

		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "List rows" ), Icon( "view_list" )]
	[Description( ".list-row and its states - odd, even, hover, selected - are the same furniture on every list in the editor." )]
	static Panel ListRows()
	{
		var example = Example.Column();

		var list = example.AddChild<Panel>( "list-demo" );

		for ( var i = 0; i < 8; i++ )
		{
			var row = list.AddChild<Panel>( "list-row" );

			row.SetClass( "even", i % 2 == 1 );
			row.SetClass( "is-selected", i == 2 );

			var icon = row.AddChild<Label>( "material-icon" );
			icon.Text = i == 2 ? "star" : "description";

			Example.Text( row, i == 2 ? "The selected row" : $"Row number {i + 1}" );
		}

		return example;
	}

	[PanelGallery, Category( Page ), Order( 4 )]
	[Title( "Type scale" ), Icon( "text_fields" )]
	[Description( "Font sizes the sheets use, on the editor face. A size not on this list is a size to think twice about." )]
	static Panel TypeScale()
	{
		var example = Example.Column();

		var scale = example.AddChild<Panel>( "type-scale" );

		foreach ( var size in new[] { 9, 10, 11, 12, 13, 14, 16, 20 } )
		{
			var label = Example.Text( scale, $"{size}px - The quick brown fox jumps over the lazy dog" );
			label.Style.FontSize = size;
		}

		Example.Text( example, ".micro - the smallest words the editor writes", "micro" );

		return example;
	}
}
