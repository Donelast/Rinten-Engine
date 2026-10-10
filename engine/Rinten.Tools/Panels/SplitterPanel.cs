using Rinten.UI;
using System;
using System.Globalization;
using System.Linq;

namespace Editor;

//What Splitter is for widgets. The cells are flex items and the shares are their grow factors, so the whole thing is laid out by flexbox and a drag only has to move a number.
/// <summary>A row or a column of panels with draggable dividers between them.</summary>
public sealed class SplitterPanel : EditorPanel
{
	//The smallest share a cell can be dragged down to.
	private const float MinimumShare = 0.05f;

	private readonly List<Panel> cells = new();
	private readonly List<float> shares = new();
	private readonly List<SplitterPanel> linked = new();

	//The dividers, where handles[i] is the one in front of cell i + 1 .
	private readonly List<SplitterHandle> handles = new();

	/// <summary>Whether the cells are stacked top to bottom rather than side by side.</summary>
	public bool IsVertical { get; }

	public SplitterPanel( bool vertical )
	{
		IsVertical = vertical;

		AddClass( "splitter-panel" );
		SetClass( "vertical", vertical );

		SetClass( "hidden", false );
	}

	/// <summary>The cells in the order they are laid out.</summary>
	public IReadOnlyList<Panel> Cells => cells;

	/// <summary>Puts a panel in as the next cell, with a divider before it where it is not the first.</summary>
	public void AddCell( Panel cell, float share = 1.0f )
	{
		if ( cell is null ) return;

		if ( cells.Count > 0 )
		{
			var handle = new SplitterHandle( this, cells.Count - 1 );

			handles.Add( handle );
			AddChild( handle );
		}

		cells.Add( cell );
		shares.Add( MathF.Max( share, MinimumShare ) );

		cell.AddClass( "splitter-cell" );
		cell.Style.FlexGrow = shares[^1];

		AddChild( cell );

		ApplyShares();
	}

	/// <summary>Hides or shows a cell with its divider, keeping the share it was dragged to</summary>
	public void SetCellShown( int index, bool shown )
	{
		if ( index < 0 || index >= cells.Count ) return;

		if ( cells[index].IsValid ) cells[index].SetClass( "hidden", !shown );

		//The one in front of it, or the one behind where it is the first cell.
		var handle = index > 0 ? index - 1 : 0;

		if ( handles.Count > handle && handles[handle].IsValid ) handles[handle].SetClass( "hidden", !shown );
	}

	/// <summary>Whether a cell is being shown.</summary>
	public bool IsCellShown( int index ) =>
		index >= 0 && index < cells.Count && cells[index].IsValid && !cells[index].HasClass( "hidden" );

	/// <summary>Move together with another splitter: dragging either one moves both, so two viewports side by side keep their edges lined up.</summary>
	public void LinkWith( SplitterPanel other )
	{
		if ( other is null || other == this ) return;

		if ( !linked.Contains( other ) ) linked.Add( other );
		if ( !other.linked.Contains( this ) ) other.linked.Add( this );
	}

	public string SaveState()
	{
		return string.Join( ' ', shares.Select( x => x.ToString( CultureInfo.InvariantCulture ) ) );
	}

	public void RestoreState( string state )
	{
		if ( string.IsNullOrWhiteSpace( state ) ) return;

		var restored = state.Split( ' ' )
			.Select( x => float.TryParse( x, NumberStyles.Float, CultureInfo.InvariantCulture, out var share ) ? share : 0.0f )
			.Where( x => x > 0.0f )
			.ToList();

		//A layout saved with a different number of cells is not this one's.
		if ( restored.Count != shares.Count ) return;

		for ( var i = 0; i < shares.Count; i++ )
		{
			shares[i] = MathF.Max( restored[i], MinimumShare );
		}

		ApplyShares();
	}

	//How long this splitter is along the axis it splits, in pixels.
	private float AxisLength => IsVertical ? Box.Rect.Height : Box.Rect.Width;

	//Moves the divider after index by so many pixels, taking from one neighbour and giving to the other.
	internal void DragHandle( int index, float pixels )
	{
		if ( index < 0 || index + 1 >= shares.Count ) return;

		var length = AxisLength;
		if ( length <= 1.0f ) return;

		var total = shares[index] + shares[index + 1];
		var moved = pixels / length * shares.Sum();

		var first = Math.Clamp( shares[index] + moved, MinimumShare, total - MinimumShare );

		shares[index] = first;
		shares[index + 1] = total - first;

		ApplyShares();

		foreach ( var other in linked )
		{
			if ( !other.IsValid ) continue;

			other.CopySharesFrom( this );
		}
	}

	//The share a cell has right now, for a drag to start from.
	internal float ShareOf( int index ) => index >= 0 && index < shares.Count ? shares[index] : 0.0f;

	private void CopySharesFrom( SplitterPanel other )
	{
		if ( other.shares.Count != shares.Count ) return;

		for ( var i = 0; i < shares.Count; i++ )
		{
			shares[i] = other.shares[i];
		}

		ApplyShares();
	}

	private void ApplyShares()
	{
		for ( var i = 0; i < cells.Count; i++ )
		{
			if ( !cells[i].IsValid ) continue;

			cells[i].Style.FlexGrow = shares[i];
		}
	}
}

//The strip between two cells of a SplitterPanel.
internal sealed class SplitterHandle : Panel
{
	private readonly SplitterPanel splitter;
	private readonly int index;

	private float pressedAt;

	public SplitterHandle( SplitterPanel splitter, int index )
	{
		this.splitter = splitter;
		this.index = index;

		AddClass( "splitter-handle" );
		SetClass( "vertical", splitter.IsVertical );
	}

	//Where along the split axis the pointer is.
	private float Along( Vector2 position ) => splitter.IsVertical ? position.y : position.x;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left ) return;

		pressedAt = Along( e.PointerPosition );

		SetClass( "dragging", true );
		SetMouseCapture( true );

		e.StopPropagation();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		if ( !HasMouseCapture ) return;

		var at = Along( e.PointerPosition );

		splitter.DragHandle( index, at - pressedAt );

		pressedAt = at;

		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		if ( !HasMouseCapture ) return;

		SetClass( "dragging", false );
		SetMouseCapture( false );

		e.StopPropagation();
	}
}
