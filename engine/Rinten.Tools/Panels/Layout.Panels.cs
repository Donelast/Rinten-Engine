using Rinten;
using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Editor;

public abstract partial class Layout
{
	//One thing in a layout: a widget, another layout, or a gap.
	internal struct Cell
	{
		public Widget Widget;
		public Layout Layout;

		/// <summary>A fixed gap, where there is neither of the above.</summary>
		public float Spacing;

		/// <summary>How much of the room left over this one takes.</summary>
		public int Stretch;

		public readonly bool IsSpacing => Widget is null && Layout is null;
	}

	internal readonly List<Cell> Cells = new();

	//Which way the cells run, and whether they run backwards.
	internal bool Vertical;
	internal bool Reversed;

	//The widget this layout arranges the inside of, if any.
	internal Widget Owner;

	//The layout this one is a cell of, if any.
	internal Layout Parent;

	//Where this layout was last told to arrange itself.
	internal Rect Area;

	internal void SetOwner( Widget widget )
	{
		Owner = widget;

		foreach ( var cell in Cells )
		{
			cell.Widget?.SetPanelParent( widget );
			cell.Layout?.SetOwner( widget );
		}
	}

	/// <summary>Hides or shows every widget in this layout subtree.</summary>
	public void SetCellsHidden( bool hidden )
	{
		foreach ( var cell in Cells )
		{
			if ( cell.Widget is { IsValid: true } widget )
				widget.Hidden = hidden;

			cell.Layout?.SetCellsHidden( hidden );
		}
	}

	//Request a re-arrange; windows do one top-down pass per frame, not immediately
	internal void OnGeometryChanged()
	{
		Owner?.MarkNeedsArrange();
		Owner?.Update();
	}

	//Puts every cell somewhere inside the rectangle.
	internal void Arrange( Rect area )
	{
		//A grid is not a row of cells; it says where each of its own goes.
		if ( this is GridLayout grid )
		{
			grid.ArrangeGrid( area );
			return;
		}

		Area = area;

		if ( Cells.Count == 0 ) return;

		var inner = area.Shrink( ManagedMargin );

		var along = Vertical ? inner.Height : inner.Width;
		var across = Vertical ? inner.Width : inner.Height;

		var gaps = ManagedSpacing * (Cells.Count - 1);
		var wanted = 0.0f;
		var stretches = 0;

		foreach ( var cell in Cells )
		{
			wanted += Wants( cell, Vertical );
			stretches += Stretch( cell );
		}

		var spread = stretches == 0;

		if ( spread )
		{
			foreach ( var cell in Cells )
			{
				if ( Grows( cell, Vertical ) ) stretches++;
			}
		}

		var spreadToAnything = spread && stretches == 0;

		if ( spreadToAnything )
		{
			foreach ( var cell in Cells )
			{
				if ( CanGrow( cell, Vertical ) ) stretches++;
			}
		}

		var spare = MathF.Max( along - wanted - gaps, 0.0f );

		//Taken from what a cell has above its minimum, not from the whole of what it asked for.
		var deficit = MathF.Max( wanted + gaps - along, 0.0f );
		var squeezable = 0.0f;

		if ( deficit > 0.0f )
		{
			foreach ( var cell in Cells )
				squeezable += Spare( cell, Vertical );
		}

		var at = Vertical ? inner.Top : inner.Left;

		//A layout told where to sit does not share out what is left over: its cells take what they asked for and the run of them is put where it was
		//told. That is what an alignment means, and it is how a toolbar keeps its buttons together at one end instead of spreading them across the window.
		var alignAlong = AlongAlignment();
		var alignAcross = AcrossAlignment();

		if ( alignAlong is { } start )
		{
			stretches = 0;
			at += spare * start;
		}

		foreach ( var cell in Cells )
		{
			var size = Wants( cell, Vertical );

			var stretch = spreadToAnything
				? (CanGrow( cell, Vertical ) ? 1 : 0)
				: spread ? (Grows( cell, Vertical ) ? 1 : 0) : Stretch( cell );

			if ( stretch > 0 && stretches > 0 )
			{
				size += spare * (stretch / (float)stretches);
			}

			if ( deficit > 0.0f && squeezable > 0.0f )
			{
				var give = Spare( cell, Vertical );

				if ( give > 0.0f )
					size = MathF.Max( size - (deficit * (give / squeezable)), Least( cell, Vertical ) );
			}

			//Across its own direction, a cell fills the layout unless it was told where to sit - a row of
			//controls centred in a taller bar keeps its own height rather than being stretched to the bar's.
			var acrossSize = across;
			var acrossAt = Vertical ? inner.Left : inner.Top;

			if ( alignAcross is { } side )
			{
				var asked = MathF.Min( Wants( cell, !Vertical ), across );

				//Nothing of its own to ask for means it is sized by whatever holds it, and that is the whole of the room.
				if ( asked > 0.5f )
				{
					acrossAt += ( across - asked ) * side;
					acrossSize = asked;
				}
			}

			var place = Vertical
				? new Rect( acrossAt, at, acrossSize, size )
				: new Rect( at, acrossAt, size, acrossSize );

			if ( Reversed )
			{
				place = Vertical
					? new Rect( inner.Left, inner.Bottom - (at - inner.Top) - size, across, size )
					: new Rect( inner.Right - (at - inner.Left) - size, inner.Top, size, across );
			}

			Place( cell, place );

			at += size + ManagedSpacing;
		}
	}

	//Where this layout was told to sit along its own direction: nought at the near edge, a half
	//in the middle, one at the far edge. Null where it was told nothing, which means fill.
	private float? AlongAlignment()
	{
		var flags = Alignment;

		if ( flags == TextFlag.None ) return null;

		if ( Vertical )
		{
			if ( flags.HasFlag( TextFlag.Top ) ) return 0.0f;
			if ( flags.HasFlag( TextFlag.Bottom ) ) return 1.0f;
			if ( flags.HasFlag( TextFlag.CenterVertically ) ) return 0.5f;

			return null;
		}

		if ( flags.HasFlag( TextFlag.Left ) ) return 0.0f;
		if ( flags.HasFlag( TextFlag.Right ) ) return 1.0f;
		if ( flags.HasFlag( TextFlag.CenterHorizontally ) ) return 0.5f;

		return null;
	}

	//The same across its direction, which is the other half of the flag.
	private float? AcrossAlignment()
	{
		var flags = Alignment;

		if ( flags == TextFlag.None ) return null;

		if ( Vertical )
		{
			if ( flags.HasFlag( TextFlag.Left ) ) return 0.0f;
			if ( flags.HasFlag( TextFlag.Right ) ) return 1.0f;
			if ( flags.HasFlag( TextFlag.CenterHorizontally ) ) return 0.5f;

			return null;
		}

		if ( flags.HasFlag( TextFlag.Top ) ) return 0.0f;
		if ( flags.HasFlag( TextFlag.Bottom ) ) return 1.0f;
		if ( flags.HasFlag( TextFlag.CenterVertically ) ) return 0.5f;

		return null;
	}

	//How much of the leftover room a cell takes. What the layout was told, or one for a widget
	//that says it wants to expand - which is the other way the editor asks for the same thing.
	private int Stretch( Cell cell )
	{
		if ( cell.Stretch > 0 ) return cell.Stretch;

		var mode = cell.Widget is null
			? SizeMode.Default
			: (Vertical ? cell.Widget.VerticalSizeMode : cell.Widget.HorizontalSizeMode);

		return mode.HasFlag( SizeMode.Expand ) ? 1 : 0;
	}

	//Whether a cell takes the room left over when nothing asked for it.
	private static bool Grows( Cell cell, bool vertical, bool alongOwner = true )
	{
		//Sized gaps stay fixed; unsized gaps stretch, but only along the layout's own axis
		if ( cell.IsSpacing ) return alongOwner && cell.Spacing <= 0.0f;

		if ( cell.Layout is { } nested )
			return nested.Cells.Any( x => Grows( x, vertical, nested.Vertical == vertical ) );

		var widget = cell.Widget;

		if ( widget is null || widget.Hidden ) return false;

		var mode = vertical ? widget.VerticalSizeMode : widget.HorizontalSizeMode;

		if ( mode.HasFlag( SizeMode.Expand ) ) return true;

		return widget.Layout is { } inside
			&& inside.Cells.Any( x => Grows( x, vertical, inside.Vertical == vertical ) );
	}

	//Whether a cell would take more room if there were any going.
	private static bool CanGrow( Cell cell, bool vertical, bool alongOwner = true )
	{
		if ( cell.IsSpacing ) return alongOwner && cell.Spacing <= 0.0f;

		if ( cell.Layout is { } nested )
			return nested.Cells.Any( x => CanGrow( x, vertical, nested.Vertical == vertical ) );

		var widget = cell.Widget;

		if ( widget is null || widget.Hidden ) return false;

		//A widget already at its maximum size cannot grow, whatever its size mode says
		var most = vertical ? widget.MaximumHeight : widget.MaximumWidth;

		if ( most > 0.0f && Wants( cell, vertical ) >= most - 0.5f ) return false;

		var mode = vertical ? widget.VerticalSizeMode : widget.HorizontalSizeMode;

		if ( mode.HasFlag( SizeMode.Expand ) ) return true;

		if ( !widget.HasSizeHint( vertical ) && widget.Layout is { Cells.Count: > 0 } inside )
			return inside.Cells.Any( x => CanGrow( x, vertical, inside.Vertical == vertical ) );

		return mode.HasFlag( SizeMode.CanGrow );
	}

	//Whether a size mode means "and it may be made smaller".
	private static bool Shrinkable( SizeMode mode )
	{
		return mode.HasFlag( SizeMode.CanShrink )
			|| mode.HasFlag( SizeMode.Expand )
			|| mode.HasFlag( SizeMode.Ignore );
	}

	//Whether a cell may be given less than it asked for.
	private static bool CanShrink( Cell cell, bool vertical )
	{
		if ( cell.IsSpacing ) return true;

		//A layout is squeezed through whatever is inside it.
		if ( cell.Layout is { } nested ) return nested.Cells.Any( x => CanShrink( x, vertical ) );

		var widget = cell.Widget;

		if ( widget is null || widget.Hidden ) return false;

		var mode = vertical ? widget.VerticalSizeMode : widget.HorizontalSizeMode;

		return Shrinkable( mode );
	}

	//How much of a cell can actually be given up, down to its minimum.
	private static float Spare( Cell cell, bool vertical )
	{
		if ( !CanShrink( cell, vertical ) ) return 0.0f;

		return MathF.Max( Wants( cell, vertical ) - Least( cell, vertical ), 0.0f );
	}

	//The least room a cell can be given, along an axis.
	private static float Least( Cell cell, bool vertical )
	{
		if ( cell.IsSpacing ) return MathF.Max( cell.Spacing, 0.0f );

		//A layout is no smaller than what is inside it needs.
		if ( cell.Layout is { } nested )
		{
			var least = 0.0f;

			foreach ( var within in nested.Cells )
			{
				var one = Least( within, vertical );

				if ( nested.Vertical == vertical ) least += one;
				else least = MathF.Max( least, one );
			}

			if ( nested.Vertical == vertical )
				least += nested.ManagedSpacing * MathF.Max( nested.Cells.Count - 1, 0 );

			var edges = nested.ManagedMargin.EdgeSize;

			return least + (vertical ? edges.y : edges.x);
		}

		var widget = cell.Widget;

		if ( widget is null || widget.Hidden ) return 0.0f;

		return vertical ? widget.MinimumHeight : widget.MinimumWidth;
	}

	//Size a cell wants along the given axis (a row's height is its tallest child, not a sum)
	internal static float WantsOf( Cell cell, bool vertical ) => Wants( cell, vertical );

	//Whether a cell would take more room if there were any going.
	internal static bool GrowsOf( Cell cell, bool vertical ) => Grows( cell, vertical );

	/// <summary>How much room this layout's own content asks for along an axis - the same reckoning used wherever this layout sits as a cell inside another one, exposed here for a top-level window that has nothing else arranging it and so has to size itself to fit what it holds.</summary>
	public float WantedSize( bool vertical )
	{
		var total = 0.0f;

		foreach ( var inner in Cells )
		{
			var want = Wants( inner, vertical );

			//Along its own direction the cells add up; across it they sit beside each other, and the biggest decides.
			if ( Vertical == vertical ) total += want;
			else total = MathF.Max( total, want );
		}

		if ( Vertical == vertical )
		{
			total += ManagedSpacing * MathF.Max( Cells.Count - 1, 0 );
		}

		var edges = ManagedMargin.EdgeSize;

		return total + (vertical ? edges.y : edges.x);
	}

	private static float Wants( Cell cell, bool vertical )
	{
		if ( cell.IsSpacing ) return cell.Spacing;

		if ( cell.Layout is GridLayout grid )
		{
			var asked = grid.WantedGrid();

			return vertical ? asked.y : asked.x;
		}

		if ( cell.Layout is { } layout )
		{
			return layout.WantedSize( vertical );
		}

		var widget = cell.Widget;
		if ( widget is null || widget.Hidden ) return 0.0f;

		//A widget never asks for more than its maximum size
		float Allowed( float wanted ) => MathF.Min( wanted,
			vertical ? widget.MaximumHeight : widget.MaximumWidth );

		//A grid is not a row: measuring it as one answers with the tallest cell.
		if ( !widget.HasSizeHint( vertical ) && widget.Layout is GridLayout owned )
		{
			var asked = owned.WantedGrid();
			var edges = widget.ContentMargins.EdgeSize;

			return Allowed( MathF.Max(
				(vertical ? asked.y : asked.x) + (vertical ? edges.y : edges.x),
				widget.MinimumWanted( vertical ) ) );
		}

		if ( !widget.HasSizeHint( vertical ) && widget.Layout is { Cells.Count: > 0 } inside )
		{
			var total = 0.0f;

			foreach ( var within in inside.Cells )
			{
				var want = Wants( within, vertical );

				if ( inside.Vertical == vertical ) total += want;
				else total = MathF.Max( total, want );
			}

			if ( inside.Vertical == vertical )
			{
				total += inside.ManagedSpacing * MathF.Max( inside.Cells.Count - 1, 0 );
			}

			//The layout's own margins, and the widget's around them: a layout is arranged
			//inside the content rect, so what the widget asks for has to leave room for both.
			var edges = inside.ManagedMargin.EdgeSize + widget.ContentMargins.EdgeSize;

			//Not LayoutWants() as well: that is this same sum worked out a second time, which is two walks of the subtree at every level
			//of it - and it counts the layout's own margin twice on top, so it always won the max and the loop above went for nothing.
			var content = total + (vertical ? edges.y : edges.x);

			return Allowed( MathF.Max( content, widget.MinimumWanted( vertical ) ) );
		}

		var mode = vertical ? widget.VerticalSizeMode : widget.HorizontalSizeMode;

		//Expanding widgets with no size hint ask only their minimum, else size ratchets up each pass
		if ( mode.HasFlag( SizeMode.Expand ) && !widget.HasSizeHint( vertical ) )
			return widget.MinimumWanted( vertical );

		//Last size came from a layout; minimum size hint still counts on this axis.
		if ( !widget.HasSizeHint( vertical ) && widget.SizeFromLayout )
			return widget.MinimumWanted( vertical );

		//What the widget says it wants, merged with what its layout needs.
		var wantedSize = widget.WantedSize();
		var axis = vertical ? wantedSize.y : wantedSize.x;
		axis = MathF.Max( axis, LayoutWants( widget, vertical ) );

		return Allowed( MathF.Max( axis, widget.MinimumWanted( vertical ) ) );
	}

	//Room a widget's own layout needs on an axis, with its margins.
	private static float LayoutWants( Widget widget, bool vertical )
	{
		if ( widget.Layout is GridLayout grid )
		{
			var asked = grid.WantedGrid();
			var edges = widget.ContentMargins.EdgeSize;

			return (vertical ? asked.y : asked.x) + (vertical ? edges.y : edges.x);
		}

		if ( widget.Layout is not { Cells.Count: > 0 } inside )
			return 0;

		var margin = inside.ManagedMargin.EdgeSize + widget.ContentMargins.EdgeSize;
		return inside.WantedSize( vertical ) + (vertical ? margin.y : margin.x);
	}

	//Puts one cell in the rectangle worked out for it.
	private static void Place( Cell cell, Rect place )
	{
		if ( cell.Layout is not null )
		{
			cell.Layout.Arrange( place );
			return;
		}

		if ( cell.Widget is null || !cell.Widget.IsValid ) return;

		cell.Widget.Position = place.Position;
		cell.Widget.Size = new Vector2(
			MathX.Clamp( place.Width, cell.Widget.MinimumWidth, cell.Widget.MaximumWidth ),
			MathX.Clamp( place.Height, cell.Widget.MinimumHeight, cell.Widget.MaximumHeight ) );

		//The size above is the room it was given, not a size it asked for.
		cell.Widget.SizeFromLayout = true;
	}


	internal float ManagedSpacing;
	internal Margin ManagedMargin;
}
