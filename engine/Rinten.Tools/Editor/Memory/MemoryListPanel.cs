using System;
using System.Collections.Generic;

namespace Editor;

//The memory dock's lines, of which only the ones the window reaches exist.
internal sealed class MemoryListPanel : PooledRowList<MemoryRowPanel>
{
	/// <summary>How tall a line is, in layout units.</summary>
	public const float LineHeight = 17.0f;

	private readonly MemoryRows rows;
	private readonly HashSet<string> expanded;

	/// <summary>A line was pressed, with what it was about.</summary>
	public Action<MemoryRow> Pressed { get; set; }

	public MemoryListPanel( MemoryRows rows, HashSet<string> expanded )
	{
		this.rows = rows;
		this.expanded = expanded;

		AddClass( "memory-list" );
	}

	protected override float RowHeight => LineHeight;

	protected override int CellCount => rows.Count;

	/// <summary>The first line the window can reach, for whatever is pinned over it.</summary>
	public int FirstVisibleLine => (int)( ScrollOffset.y * ScaleFromScreen / LineHeight );

	protected override MemoryRowPanel CreateCell() => new() { Clicked = OnPressed };

	protected override void BindCell( MemoryRowPanel cell, int index )
	{
		var row = rows[index];

		cell.Index = index;
		cell.Bind( row, row.Foldable && expanded.Contains( row.Key ), index );
	}

	protected override void UnbindCell( MemoryRowPanel cell ) => cell.Unbind();

	private void OnPressed( PanelPooledRow cell )
	{
		if ( cell.Index < 0 || cell.Index >= rows.Count ) return;

		Pressed?.Invoke( rows[cell.Index] );
	}
}
