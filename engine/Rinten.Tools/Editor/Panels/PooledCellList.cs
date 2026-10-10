using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>Virtual list of same-size cells reused by index; cells must not tick and BindCell writes only diffs</summary>
public abstract class PooledCellList<TCell> : Panel where TCell : Panel
{
	//What the cells are placed in; its height is what the scroll measures.
	protected Panel Content { get; }

	private readonly Dictionary<int, TCell> live = new();
	private readonly Stack<TCell> free = new();
	private readonly List<int> leaving = new();

	private int composition;
	private int builtComposition = -1;
	private int builtCount = -1;
	private float builtWidth = float.NaN;
	private float builtHeight = float.NaN;
	private float builtScroll = float.NaN;
	private float contentHeight = float.NaN;

	//Which phase of a spread refresh is due, or -1 when none is.
	private int refreshPhase = -1;

	protected PooledCellList()
	{
		AddClass( "pooled-cell-list" );

		Content = AddChild<Panel>( "content" );

		EditorStyles.Apply( this, "PooledCellList.cs.scss" );
	}

	//How many cells are kept either side of the window.
	protected virtual int Overscan => 2;

	//How many frames a Refresh is spread over, so one frame does not carry every changed value at once.
	protected virtual int RefreshPhases => 1;

	//How many cells there are to show.
	protected abstract int CellCount { get; }

	//A cell with nothing on it yet. Called only when the pool is empty.
	protected abstract TCell CreateCell();

	//Puts a cell over an index. Write only what differs - this runs for every live cell when the composition changes.
	protected abstract void BindCell( TCell cell, int index );

	//The same cells over the same indices, with values that may have moved.
	protected virtual void RefreshCell( TCell cell, int index ) { }

	//A cell leaving the window; whatever it was holding is dropped here.
	protected virtual void UnbindCell( TCell cell ) { }

	//Reads the width the cells are laid into, before the three questions below are asked.
	protected abstract void MeasureCells( float width );

	//How tall the whole list is, in layout units.
	protected abstract float TotalHeight( int count );

	//The first index the window can reach, and one past the last. Overscan is the layout's to apply.
	protected abstract void GetWindow( float scrollTop, float viewHeight, int count, out int first, out int pastEnd );

	//Where a cell sits, in layout units from the content's top left.
	protected abstract void PositionCell( TCell cell, int index );

	//Where a cell starts and how tall it is, for scrolling one into view.
	protected abstract void CellExtent( int index, out float top, out float height );

	/// <summary>The cells are over the wrong indices now: what is at each one may have changed.</summary>
	public void Invalidate() => composition++;

	/// <summary>The cells are over the right indices; what they show has moved.</summary>
	public void Refresh() => refreshPhase = 0;

	//The cell over an index, where that index is on screen.
	protected bool TryGetCell( int index, out TCell cell ) => live.TryGetValue( index, out cell );

	public override void Tick()
	{
		base.Tick();

		if ( ComputedStyle is null || !IsVisible ) return;

		var scale = ScaleFromScreen;
		var width = Box.RectInner.Width * scale;

		//Before the first layout there is no box, and every index would land in the same place.
		if ( width <= 1.0f ) return;

		var height = Box.Rect.Height * scale;
		var scroll = ScrollOffset.y * scale;
		var count = CellCount;

		var metricsMoved = width != builtWidth;
		var structureMoved = composition != builtComposition || count != builtCount;

		if ( metricsMoved ) MeasureCells( width );

		if ( metricsMoved || structureMoved ) SetContentHeight( TotalHeight( count ) );

		if ( metricsMoved || structureMoved || scroll != builtScroll || height != builtHeight )
		{
			UpdateWindow( scroll, height, count, rebind: structureMoved, reposition: metricsMoved );

			builtWidth = width;
			builtHeight = height;
			builtScroll = scroll;
			builtCount = count;
			builtComposition = composition;
		}

		RunRefresh();
	}

	/// <summary>Brings a cell into view, moving as little as it takes.</summary>
	public void ScrollToCell( int index )
	{
		if ( index < 0 || index >= CellCount ) return;

		CellExtent( index, out var top, out var height );

		var scale = ScaleFromScreen;
		var view = Box.Rect.Height * scale;
		var scroll = ScrollOffset.y * scale;

		var wanted = scroll;

		if ( top < scroll ) wanted = top;
		else if ( top + height > scroll + view ) wanted = top + height - view;

		if ( wanted == scroll ) return;

		ScrollOffset = new Vector2( ScrollOffset.x, MathF.Max( 0.0f, wanted ) * ScaleToScreen );
	}

	private void UpdateWindow( float scroll, float viewHeight, int count, bool rebind, bool reposition )
	{
		var first = 0;
		var pastEnd = 0;

		if ( count > 0 )
		{
			GetWindow( scroll, viewHeight, count, out first, out pastEnd );

			first = Math.Max( 0, first );
			pastEnd = Math.Min( count, pastEnd );
		}

		leaving.Clear();

		foreach ( var pair in live )
		{
			if ( pair.Key < first || pair.Key >= pastEnd ) leaving.Add( pair.Key );
		}

		for ( var at = 0; at < leaving.Count; ++at )
		{
			if ( !live.Remove( leaving[at], out var cell ) ) continue;

			UnbindCell( cell );

			cell.Style.Display = DisplayMode.None;
			free.Push( cell );
		}

		leaving.Clear();

		for ( var index = first; index < pastEnd; ++index )
		{
			if ( live.TryGetValue( index, out var living ) )
			{
				if ( rebind ) BindCell( living, index );
				if ( reposition ) PositionCell( living, index );

				continue;
			}

			var cell = Take();

			live[index] = cell;

			BindCell( cell, index );
			PositionCell( cell, index );
		}
	}

	private TCell Take()
	{
		if ( free.TryPop( out var pooled ) )
		{
			//Back to whatever the sheet says it is, rather than to a guess at it.
			pooled.Style.Display = null;

			return pooled;
		}

		var cell = CreateCell();

		cell.Style.Position = PositionMode.Absolute;

		Content.AddChild( cell );

		return cell;
	}

	private void RunRefresh()
	{
		if ( refreshPhase < 0 ) return;

		var phases = Math.Max( 1, RefreshPhases );

		foreach ( var pair in live )
		{
			if ( phases > 1 && pair.Key % phases != refreshPhase ) continue;

			RefreshCell( pair.Value, pair.Key );
		}

		refreshPhase++;

		if ( refreshPhase >= phases ) refreshPhase = -1;
	}

	private void SetContentHeight( float height )
	{
		if ( MathF.Abs( contentHeight - height ) < 0.5f ) return;

		contentHeight = height;

		Content.Style.Height = Length.Pixels( height );
	}
}
