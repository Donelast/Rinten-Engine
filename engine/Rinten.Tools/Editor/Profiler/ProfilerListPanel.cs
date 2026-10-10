using System;
using System.Collections.Generic;

namespace Editor;

//The profiler's lines, of which only the ones the window reaches exist.
internal sealed class ProfilerListPanel : PooledRowList<ProfilerRowPanel>
{
	/// <summary>How tall a line is, in layout units.</summary>
	public const float LineHeight = 17.0f;

	private readonly ProfilerRows rows;
	private readonly HashSet<int> collapsed;

	/// <summary>A line was pressed, with what it was about.</summary>
	public Action<ProfilerRow> Pressed { get; set; }

	public ProfilerListPanel( ProfilerRows rows, HashSet<int> collapsed )
	{
		this.rows = rows;
		this.collapsed = collapsed;

		AddClass( "profiler-list" );
	}

	protected override float RowHeight => LineHeight;

	protected override int CellCount => rows.Count;

	/// <summary>The first line the window can reach, for whatever is pinned over it.</summary>
	public int FirstVisibleLine => (int)( ScrollOffset.y * ScaleFromScreen / LineHeight );

	protected override ProfilerRowPanel CreateCell() => new() { Clicked = OnPressed };

	protected override void BindCell( ProfilerRowPanel cell, int index )
	{
		var row = rows[index];

		cell.Index = index;
		cell.Bind( row, IsFolded( row ), index );
	}

	protected override void RefreshCell( ProfilerRowPanel cell, int index )
	{
		if ( index >= rows.Count ) return;

		cell.Refresh( rows[index] );
	}

	protected override void UnbindCell( ProfilerRowPanel cell ) => cell.Unbind();

	private bool IsFolded( in ProfilerRow row ) => row.Foldable && collapsed.Contains( row.Entry.Id );

	private void OnPressed( PanelPooledRow cell )
	{
		if ( cell.Index < 0 || cell.Index >= rows.Count ) return;

		Pressed?.Invoke( rows[cell.Index] );
	}
}
