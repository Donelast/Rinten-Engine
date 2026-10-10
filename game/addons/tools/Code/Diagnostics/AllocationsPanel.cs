using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>What is being allocated while the recorder is running, by type.</summary>
[StyleSheet( "AllocationsPanel.cs.scss" )]
public sealed class AllocationsPanel : Panel
{
	//How often the list is worked out again, in seconds.
	private const float Interval = 0.5f;

	//How many types are worth showing at once.
	private const int Ceiling = 256;

	private readonly Rinten.Diagnostics.Allocations.Scope scope = new();
	private readonly List<Rinten.Diagnostics.Allocations.Entry> entries = new();

	private readonly AllocationsListPanel list;
	private readonly PanelToolbarButton record;

	private ulong scale = 1;
	private RealTimeSince timer;

	public AllocationsPanel()
	{
		AddClass( "allocations" );

		var toolbar = AddChild<PanelToolbar>();

		record = toolbar.AddToggle( "fiber_manual_record", "Record what is allocated, sampled every ~100 KB, by type", false, SetRecording );

		toolbar.AddIcon( "delete", "Forget what has been recorded so far", () => { scope.Clear(); Rebuild(); } );

		var header = AddChild( new PanelListHeader( "type" ) );
		header.AddColumn( "count" );
		header.AddColumn( "bytes" );

		var body = AddChild<Panel>( "body" );

		list = new AllocationsListPanel( entries, () => scale );
		body.AddChild( list );
	}

	public override void Tick()
	{
		base.Tick();

		if ( !IsVisible ) return;
		if ( timer < Interval ) return;

		timer = 0.0f;

		Rebuild();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		scope.Stop();
	}

	private void SetRecording( bool on )
	{
		if ( on ) scope.Start();
		else scope.Stop();

		record.Tooltip = on ? "Stop recording" : "Record what is allocated, sampled every ~100 KB, by type";
	}

	private void Rebuild()
	{
		var wanted = scope.Entries
			.OrderByDescending( x => x.Count )
			.Take( Ceiling )
			.ToList();

		scale = Math.Max( 1UL, wanted.Count > 0 ? wanted[0].Count : 1UL );

		if ( Same( wanted ) ) return;

		entries.Clear();
		entries.AddRange( wanted );

		list.Invalidate();
	}

	private bool Same( List<Rinten.Diagnostics.Allocations.Entry> wanted )
	{
		if ( wanted.Count != entries.Count ) return false;

		for ( var at = 0; at < wanted.Count; ++at )
		{
			if ( wanted[at].Count != entries[at].Count ) return false;
			if ( wanted[at].TotalBytes != entries[at].TotalBytes ) return false;
			if ( !string.Equals( wanted[at].Name, entries[at].Name, StringComparison.Ordinal ) ) return false;
		}

		return true;
	}
}

/// <summary>The types, of which only the ones the window reaches exist.</summary>
public sealed class AllocationsListPanel : PooledRowList<AllocationsRowPanel>
{
	private readonly List<Rinten.Diagnostics.Allocations.Entry> entries;
	private readonly Func<ulong> scale;

	public AllocationsListPanel( List<Rinten.Diagnostics.Allocations.Entry> entries, Func<ulong> scale )
	{
		this.entries = entries;
		this.scale = scale;

		AddClass( "allocations-list" );
	}

	protected override float RowHeight => 17.0f;

	protected override int CellCount => entries.Count;

	protected override AllocationsRowPanel CreateCell() => new();

	protected override void BindCell( AllocationsRowPanel cell, int index )
	{
		cell.Index = index;
		cell.Bind( entries[index], scale(), index );
	}
}

/// <summary>One type: how many were sampled, and how much of it.</summary>
public sealed class AllocationsRowPanel : PanelListRow
{
	public AllocationsRowPanel() : base( 2 )
	{
		AddClass( "allocations-row" );

		Column( 0 ).AddClass( "count" );
		Column( 1 ).AddClass( "bytes" );
	}

	public void Bind( Rinten.Diagnostics.Allocations.Entry entry, ulong scale, int band )
	{
		SetBand( band );
		SetName( entry.Name );
		SetBar( scale > 0 ? (float)( (double)entry.Count / scale ) : 0.0f );

		PanelText.Count( Column( 0 ), (long)entry.Count, "N0" );
		PanelText.Bytes( Column( 1 ), (long)entry.TotalBytes );
	}
}
