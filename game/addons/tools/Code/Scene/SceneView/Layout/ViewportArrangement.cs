using Rinten.UI;

namespace Editor;

//Makes the viewports an arrangement is built out of.
internal interface IViewportSource
{
	//A new viewport, numbered in the order they are made unless told otherwise.
	Panel CreateViewport( int id = -1 );

	//A splitter, kept so its position can be saved and restored.
	SplitterPanel CreateSplitter( bool vertical );
}

//One way of dividing a scene view up into viewports. A class each rather than a switch,
//so a new arrangement is a new file and nothing that already works is opened to add it.
internal interface IViewportArrangement
{
	ViewportLayoutMode Mode { get; }

	//Builds the arrangement and returns what goes in the scene view.
	Panel Build( IViewportSource source );
}

//Every arrangement there is, by the mode that asks for it.
internal static class ViewportArrangements
{
	private static readonly IViewportArrangement[] All =
	{
		new OneViewport(),
		new TwoHorizontalViewports(),
		new TwoVerticalViewports(),
		new ThreeLeftViewports(),
		new ThreeRightViewports(),
		new ThreeTopViewports(),
		new ThreeBottomViewports(),
		new FourViewports(),
	};

	public static IViewportArrangement For( ViewportLayoutMode mode )
	{
		return All.FirstOrDefault( x => x.Mode == mode ) ?? All[0];
	}
}

internal sealed class OneViewport : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.One;

	public Panel Build( IViewportSource source ) => source.CreateViewport();
}

internal sealed class TwoHorizontalViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.TwoHorizontal;

	public Panel Build( IViewportSource source )
	{
		var row = source.CreateSplitter( vertical: false );
		row.AddCell( source.CreateViewport() );
		row.AddCell( source.CreateViewport() );
		return row;
	}
}

internal sealed class TwoVerticalViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.TwoVertical;

	public Panel Build( IViewportSource source )
	{
		var column = source.CreateSplitter( vertical: true );
		column.AddCell( source.CreateViewport() );
		column.AddCell( source.CreateViewport() );
		return column;
	}
}

internal sealed class ThreeLeftViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.ThreeLeft;

	public Panel Build( IViewportSource source )
	{
		var column = source.CreateSplitter( vertical: true );
		column.AddCell( source.CreateViewport( 1 ) );
		column.AddCell( source.CreateViewport( 2 ) );

		var row = source.CreateSplitter( vertical: false );
		row.AddCell( source.CreateViewport( 0 ) );
		row.AddCell( column );

		return row;
	}
}

internal sealed class ThreeRightViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.ThreeRight;

	public Panel Build( IViewportSource source )
	{
		var column = source.CreateSplitter( vertical: true );
		column.AddCell( source.CreateViewport( 1 ) );
		column.AddCell( source.CreateViewport( 2 ) );

		var row = source.CreateSplitter( vertical: false );
		row.AddCell( column );
		row.AddCell( source.CreateViewport( 0 ) );

		return row;
	}
}

internal sealed class ThreeTopViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.ThreeTop;

	public Panel Build( IViewportSource source )
	{
		var column = source.CreateSplitter( vertical: true );
		column.AddCell( source.CreateViewport() );

		var row = source.CreateSplitter( vertical: false );
		row.AddCell( source.CreateViewport() );
		row.AddCell( source.CreateViewport() );

		column.AddCell( row );

		return column;
	}
}

internal sealed class ThreeBottomViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.ThreeBottom;

	public Panel Build( IViewportSource source )
	{
		var row = source.CreateSplitter( vertical: false );
		row.AddCell( source.CreateViewport( 1 ) );
		row.AddCell( source.CreateViewport( 2 ) );

		var column = source.CreateSplitter( vertical: true );
		column.AddCell( row );
		column.AddCell( source.CreateViewport( 0 ) );

		return column;
	}
}

internal sealed class FourViewports : IViewportArrangement
{
	public ViewportLayoutMode Mode => ViewportLayoutMode.Four;

	public Panel Build( IViewportSource source )
	{
		var left = source.CreateSplitter( vertical: true );
		left.AddCell( source.CreateViewport() );
		left.AddCell( source.CreateViewport() );

		var right = source.CreateSplitter( vertical: true );
		right.AddCell( source.CreateViewport() );
		right.AddCell( source.CreateViewport() );

		var row = source.CreateSplitter( vertical: false );
		row.AddCell( left );
		row.AddCell( right );

		//Dragging either column's divider moves the other, so the four stay lined up across the middle.
		left.LinkWith( right );

		return row;
	}
}
