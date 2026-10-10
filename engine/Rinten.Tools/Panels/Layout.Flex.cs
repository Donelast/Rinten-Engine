using Rinten.UI;

namespace Editor;

/// <summary>A layout as a flex container.</summary>
public abstract partial class Layout
{
	//The panel this layout is, where it is not the one its widget already has.
	internal LayoutPanel FlexBox;

	//Gaps of a stated size, as panels, in cell order.
	private readonly List<Panel> spacers = new();

	//Says what this layout is, and what everything in it is.
	internal void Flex( Panel parent, Margin extra = default )
	{
		if ( parent is null ) return;
		if ( !Enabled ) return;

		var box = Container( parent );

		if ( box is null ) return;

		//Flexbox has no grid, so a grid places its own cells inside a box that is a normal flex item
		if ( this is GridLayout grid )
		{
			grid.ArrangeGrid( FlexArea( box ) );
			return;
		}

		Describe( box, extra );
		Fill( box );
	}

	//The room this layout has, in the coordinates its cells are placed in - which are the owning widget's, because that is what they hang from.
	private Rect FlexArea( Panel box )
	{
		//The outermost layout fills its widget, and the widget already knows how big it is.
		if ( Parent is null && Owner is not null ) return Owner.ContentRect;

		var scale = box.ScaleFromScreen;
		var mine = box.Box.Rect;
		var owner = Owner?.Surface?.Box.Rect.Position ?? Vector2.Zero;

		return new Rect( (mine.Position - owner) * scale, mine.Size * scale );
	}

	//The panel that holds this layout's cells. The layout a widget owns uses the widget's own panel: one less box in the tree, and the widget's
	//rectangle is the layout's to fill anyway. A layout inside another one gets a panel of its own, because flex has to have something to arrange.
	private Panel Container( Panel parent )
	{
		if ( Parent is null && Owner?.Surface is { } surface ) return surface;

		FlexBox ??= new LayoutPanel( this );

		if ( FlexBox.Parent != parent ) parent.AddChild( FlexBox );

		return FlexBox;
	}

	//Which way it runs, what it leaves around itself, where it sits.
	private void Describe( Panel box, Margin extra )
	{
		box.Style.FlexDirection = Vertical
			? (Reversed ? FlexDirection.ColumnReverse : FlexDirection.Column)
			: (Reversed ? FlexDirection.RowReverse : FlexDirection.Row);

		box.Style.RowGap = ManagedSpacing;
		box.Style.ColumnGap = ManagedSpacing;

		box.Style.PaddingLeft = ManagedMargin.Left + extra.Left;
		box.Style.PaddingTop = ManagedMargin.Top + extra.Top;
		box.Style.PaddingRight = ManagedMargin.Right + extra.Right;
		box.Style.PaddingBottom = ManagedMargin.Bottom + extra.Bottom;

		box.Style.JustifyContent = AlongAlignment() switch
		{
			0.0f => Justify.FlexStart,
			0.5f => Justify.Center,
			1.0f => Justify.FlexEnd,
			_ => Justify.FlexStart
		};

		box.Style.AlignItems = Across();
	}

	//Where a cell sits across the layout's own direction.
	private Align Across()
	{
		return AcrossAlignment() switch
		{
			0.0f => Align.FlexStart,
			0.5f => Align.Center,
			1.0f => Align.FlexEnd,

			//Nothing said, so it fills - which is what a cell does across a layout that was not told where to sit.
			_ => Align.Stretch
		};
	}

	//Puts every cell in the container and says what each one is.
	private void Fill( Panel box )
	{
		//Who takes the room left over, worked out exactly the way the arranging code works it out: what
		//asked for it by name, or failing that whatever wants to grow, or failing that whatever merely can.
		var stretches = 0;

		foreach ( var cell in Cells ) stretches += Stretch( cell );

		var spread = stretches == 0;

		if ( spread )
		{
			foreach ( var cell in Cells )
			{
				if ( Grows( cell, Vertical ) ) stretches++;
			}
		}

		var spreadToAnything = spread && stretches == 0;

		//A layout told where to sit does not share out what is left over: its cells take what they
		//asked for and the run of them is put where it was told. That is what an alignment means.
		var packed = AlongAlignment() is not null;

		var ordered = new List<Panel>( Cells.Count );

		for ( var i = 0; i < Cells.Count; i++ )
		{
			var cell = Cells[i];

			var grow = packed ? 0 : spreadToAnything
				? (CanGrow( cell, Vertical ) ? 1 : 0)
				: spread ? (Grows( cell, Vertical ) ? 1 : 0) : Stretch( cell );

			if ( cell.Layout is GridLayout )
				grow = cell.Stretch > 0 ? cell.Stretch : 0;

			if ( Item( box, cell, i, grow ) is { } panel ) ordered.Add( panel );
		}

		Reorder( box, ordered );
	}

	//Reorder child panels to match cell order, only when wrong, since re-adding triggers layout
	private static void Reorder( Panel box, List<Panel> ordered )
	{
		if ( ordered.Count < 2 ) return;

		//Where the run of cells starts among the container's children. A widget may be holding panels that are
		//not cells at all - its own markup, or a widget standing in for one - and those are left where they are.
		var first = int.MaxValue;

		foreach ( var panel in ordered )
		{
			var index = box.GetChildIndex( panel );

			if ( index >= 0 && index < first ) first = index;
		}

		if ( first == int.MaxValue ) return;

		//Moving a child that is already in the right place does nothing, so this costs nothing on every frame after the first.
		for ( var i = 0; i < ordered.Count; i++ )
		{
			if ( ordered[i].Parent != box ) continue;

			box.SetChildIndex( ordered[i], first + i );
		}
	}

	//One cell: a layout, a widget, or a gap.
	private Panel Item( Panel box, Cell cell, int index, float grow )
	{
		if ( cell.Layout is { } nested )
		{
			nested.Flex( box );

			Describe( nested.FlexBox, cell, grow );

			return nested.FlexBox;
		}

		if ( cell.Widget is { } widget )
		{
			if ( widget.Surface is null ) return null;
			if ( !widget.IsValid ) return null;

			if ( widget.Surface.Parent != box ) box.AddChild( widget.Surface );

			widget.InFlex = true;

			Describe( widget.Surface, cell, grow );

			return widget.Surface;
		}

		var spacer = Spacer( box, index );

		Describe( spacer, cell, grow );

		return spacer;
	}

	//A gap, as a panel that is nothing but room.
	private Panel Spacer( Panel box, int index )
	{
		while ( spacers.Count <= index ) spacers.Add( null );

		var spacer = spacers[index];

		if ( spacer is null || !spacer.IsValid() )
		{
			spacer = new Panel();

			spacers[index] = spacer;
		}

		if ( spacer.Parent != box ) box.AddChild( spacer );

		return spacer;
	}

	//What one cell is, as a flex item.
	private void Describe( Panel panel, Cell cell, float grow )
	{
		if ( panel is null ) return;

		//Flex items must drop absolute position and size left over from widget placement
		panel.Style.Position = PositionMode.Relative;
		panel.Style.Left = null;
		panel.Style.Top = null;
		panel.Style.Width = null;
		panel.Style.Height = null;

		panel.Style.FlexGrow = grow;
		panel.Style.FlexShrink = CanShrink( cell, Vertical ) ? 1.0f : 0.0f;
		panel.Style.FlexBasis = Wants( cell, Vertical );

		panel.Style.AlignSelf = Across();

		if ( cell.Widget is { } widget ) Constrain( panel, widget );
	}

	//How big a widget is allowed to be, whatever the layout would like.
	private static void Constrain( Panel panel, Widget widget )
	{
		panel.Style.MinWidth = widget.MinimumWidth > 0.0f ? widget.MinimumWidth : null;
		panel.Style.MinHeight = widget.MinimumHeight > 0.0f ? widget.MinimumHeight : null;

		//The largest a widget may be means no limit at all, and saying so as a length is a widget the size of a number nobody meant.
		panel.Style.MaxWidth = widget.MaximumWidth < Unbounded ? widget.MaximumWidth : null;
		panel.Style.MaxHeight = widget.MaximumHeight < Unbounded ? widget.MaximumHeight : null;
	}

	//Past this, a maximum is not a maximum.
	private const float Unbounded = 100000.0f;

	//Lets go of the panels this layout made, when it is emptied.
	internal void ForgetFlex()
	{
		foreach ( var spacer in spacers )
		{
			spacer?.Delete( true );
		}

		spacers.Clear();
	}
}

//The panel a layout is, when a layout is a flex container. Only for the ones inside another layout: the outermost
//layout uses the panel its widget already has, because a widget's rectangle is exactly the room its layout has to fill.
internal sealed class LayoutPanel : Panel
{
	public Layout Layout { get; }

	public LayoutPanel( Layout layout )
	{
		Layout = layout;
	}

	//The size this box was last handed to the layout at.
	private Vector2 arrangedAt = new( -1.0f, -1.0f );

	/// <summary>Asks for the layout again when the room it is arranged into changes size.</summary>
	public override void FinalLayout( Vector2 offset )
	{
		base.FinalLayout( offset );

		//The settle passes are flex converging on an answer. Asking for the layout again from inside one is
		//asking it to converge on top of itself; the pass that follows reads the size we would have reported.
		if ( Widget.FlexSettling > 0 ) return;

		var size = Box.Rect.Size;

		if ( size.AlmostEqual( arrangedAt ) ) return;

		arrangedAt = size;

		Layout?.Owner?.MarkNeedsArrange();
	}
}
