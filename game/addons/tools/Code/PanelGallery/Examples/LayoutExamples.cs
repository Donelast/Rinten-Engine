using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//How panels are arranged when there are many of them: splitters, virtual lists, trees, scrolling.
static class LayoutExamples
{
	const string Page = "Layout";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Splitter" ), Icon( "view_column" )]
	[Description( "SplitterPanel: cells with a share each, and a handle between them to drag. A vertical one nested in the right cell." )]
	static Panel Splitters()
	{
		var example = Example.Column();

		var splitter = example.AddChild( new SplitterPanel( vertical: false ) );
		splitter.AddClass( "fixed-height" );

		var left = new Panel();
		Example.Tile( left, "share 1", "well" );
		splitter.AddCell( left, 1 );

		var nested = new SplitterPanel( vertical: true );

		var top = new Panel();
		Example.Tile( top, "top, share 2", "well" );
		nested.AddCell( top, 2 );

		var bottom = new Panel();
		Example.Tile( bottom, "bottom, share 1", "well" );
		nested.AddCell( bottom, 1 );

		splitter.AddCell( nested, 2 );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Virtual list" ), Icon( "format_list_numbered" )]
	[Description( "VirtualList over a hundred thousand rows: only the rows in view exist. What the asset browser and the hierarchy are made of." )]
	static Panel VirtualLists()
	{
		var example = Example.Column();

		var list = example.AddChild<VirtualList>();
		list.AddClass( "fixed-height" );
		list.AddClass( "list-demo" );
		list.ItemHeight = 22;

		list.OnCreateCell = ( cell, item ) =>
		{
			cell.AddClass( "list-row" );

			var icon = cell.AddChild<Label>( "material-icon" );
			icon.Text = "description";

			Example.Text( cell, item.ToString() );
		};

		list.Items = Enumerable.Range( 1, 100_000 ).Select( x => (object)$"Row number {x}" );

		Example.Note( example, "Scroll to the bottom; it should cost the same as the top." );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Tree" ), Icon( "account_tree" )]
	[Description( "TreePanel over a made-up tree three levels deep. Expand, select, and press the second row's chevron." )]
	static Panel Trees()
	{
		var example = Example.Column();

		var tree = example.AddChild<SampleTreePanel>();
		tree.AddClass( "tall-height" );
		tree.AddClass( "list-demo" );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "Scroll" ), Icon( "swap_vert" )]
	[Description( "A .scroll box of fixed height with more in it than fits." )]
	static Panel Scrolling()
	{
		var example = Example.Column();

		var box = example.AddChild<Panel>( "scroll fixed-height list-demo" );

		for ( var i = 1; i <= 60; i++ )
		{
			var row = box.AddChild<Panel>( "list-row" );
			row.SetClass( "even", i % 2 == 0 );
			Example.Text( row, $"Line {i} of 60" );
		}

		return example;
	}

	/// <summary>A node in the sample tree.</summary>
	public sealed class SampleNode
	{
		public string Name { get; init; }
		public List<SampleNode> Children { get; } = new();
	}

	/// <summary>A TreePanel over <see cref="SampleNode"/>: the smallest tree there is, for seeing the rows and chevrons.</summary>
	public sealed class SampleTreePanel : TreePanel<SampleNode>
	{
		readonly List<SampleNode> roots;

		public SampleTreePanel()
		{
			roots = Enumerable.Range( 1, 4 ).Select( i => Make( $"Root {i}", 3 ) ).ToList();

			SelectionMode = TreeSelectionMode.Multi;
		}

		static SampleNode Make( string name, int depth )
		{
			var node = new SampleNode { Name = name };

			if ( depth <= 0 ) return node;

			for ( var i = 1; i <= 3; i++ )
				node.Children.Add( Make( $"{name}.{i}", depth - 1 ) );

			return node;
		}

		protected override int Revision => 1;
		protected override IEnumerable<SampleNode> Roots => roots;
		protected override IEnumerable<SampleNode> GetChildren( SampleNode node ) => node.Children;

		protected override TreeItemInfo GetInfo( SampleNode node )
			=> new( node.Name, node.Children.Count > 0 ? "folder" : "description", node.Children.Count > 0 );
	}
	[PanelGallery, Category( Page ), Order( 9 )]
	[Title( "Block flow" ), Icon( "view_agenda" )]
	[Description( "display: block. Tiles stack down the container and fill its width unless they say otherwise; vertical margins collapse the way CSS 2 has them, and automatic side margins centre a tile." )]
	static Panel BlockFlow()
	{
		var example = Example.Column();

		var block = example.AddChild<Panel>( "well" );
		block.Style.Display = DisplayMode.Block;
		block.Style.PaddingTop = Length.Pixels( 8 );
		block.Style.PaddingBottom = Length.Pixels( 8 );

		var first = Example.Tile( block, "margin-bottom 24" );
		first.Style.MarginBottom = Length.Pixels( 24 );

		var second = Example.Tile( block, "margin-top 16: the gap is 24, not 40" );
		second.Style.MarginTop = Length.Pixels( 16 );

		var centred = Example.Tile( block, "width 160, side margins auto" );
		centred.Style.Width = Length.Pixels( 160 );
		centred.Style.MarginLeft = Length.Auto;
		centred.Style.MarginRight = Length.Auto;
		centred.Style.MarginTop = Length.Pixels( 8 );

		var wrapper = block.AddChild<Panel>();
		wrapper.Style.Display = DisplayMode.Block;
		wrapper.Style.MarginTop = Length.Pixels( 8 );

		var inner = Example.Tile( wrapper, "margin-top 32 inside a block with no padding: it passes out through the top" );
		inner.Style.MarginTop = Length.Pixels( 32 );

		Example.Note( example, "The last tile sits 32 below the centred one, not 8 + 32: its margin left its parent and collapsed with the parent's." );

		return example;
	}
	[PanelGallery, Category( Page ), Order( 10 )]
	[Title( "Inspector grid" ), Icon( "grid_on" )]
	[Description( "display: grid with grid-template-columns: 120px 1fr auto. Every row is one grid: the label column is fixed, the control takes what is left, the button is as wide as it is. The columns line up across rows because each row is the same grid." )]
	static Panel InspectorGrid()
	{
		var example = Example.Column();

		var names = new[] { "Position", "Rotation", "Scale", "Tint" };
		var controls = new[] { "0, 0, 0", "0, 90, 0", "1, 1, 1", "255, 255, 255" };
		var buttons = new[] { "reset", "copy", "reset", "pick colour" };
		var icons = new[] { "refresh", "content_copy", "refresh", "palette" };

		for ( var i = 0; i < names.Length; i++ )
		{
			var row = example.AddChild<Panel>();
			row.Style.Display = DisplayMode.Grid;
			row.Style.GridTemplateColumns = "120px 1fr auto";
			row.Style.ColumnGap = Length.Pixels( 8 );
			row.Style.AlignItems = Rinten.UI.Align.Center;
			row.Style.PaddingTop = Length.Pixels( 2 );
			row.Style.PaddingBottom = Length.Pixels( 2 );

			Example.Text( row, names[i] );
			Example.Tile( row, controls[i], "well" );
			Example.Button( row, buttons[i], icons[i], () => { } );
		}

		Example.Note( example, "Resize the stage: the middle column takes the change, the other two do not move." );

		return example;
	}
}
