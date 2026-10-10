using System;

namespace Editor;

//One line of the memory dock: a world, a type on the heap, a referrer, or a heading.
internal sealed class MemoryRowPanel : PanelListRow
{
	//How many number columns a line has: bytes, growth, count, growth.
	private const int Columns = 4;

	//Growth past this much is the loud kind, per column.
	private const long LoudBytes = 16 * 1024 * 1024;
	private const long LoudCount = 1024;
	private const long LoudPerSecond = 16;

	private int bytesTone = -1;
	private int countTone = -1;

	public MemoryRowPanel() : base( Columns )
	{
		AddClass( "memory-row" );

		Column( 0 ).AddClass( "bytes" );
		Column( 1 ).AddClass( "grow" );
		Column( 2 ).AddClass( "count" );
		Column( 3 ).AddClass( "grow" );
	}

	/// <summary>Puts this row over a line.</summary>
	public void Bind( in MemoryRow row, bool folded, int band )
	{
		SetBand( band );
		SetSection( row.Kind == MemoryRowKind.Section );
		SetDepth( row.Depth );
		SetChevron( row.Foldable, folded );
		SetName( row.Name );
		SetBar( row.Portion );

		switch ( row.Kind )
		{
			case MemoryRowKind.Heap: BindHeap( row ); break;
			case MemoryRowKind.Scene: BindScene( row ); break;
			case MemoryRowKind.Referrer: BindReferrer( row ); break;
			default: BindSection(); break;
		}
	}

	/// <summary>Drops what this row was holding while it waits out of the window.</summary>
	public void Unbind()
	{
		Index = -1;

		SetName( string.Empty );
	}

	private void BindHeap( in MemoryRow row )
	{
		PanelText.Bytes( Column( 0 ), row.Bytes );
		PanelText.Bytes( Column( 1 ), row.DeltaBytes, signed: true );
		PanelText.Count( Column( 2 ), row.Count, "N0" );

		if ( row.DeltaCount != 0 ) PanelText.Count( Column( 3 ), row.DeltaCount, "N0", prefix: row.DeltaCount > 0 ? "+" : "" );
		else PanelText.Set( Column( 3 ), string.Empty );

		SetBytesTone( Tone( row.DeltaBytes, LoudBytes ) );
		SetCountTone( Tone( row.DeltaCount, LoudCount ) );
	}

	private void BindScene( in MemoryRow row )
	{
		PanelText.Set( Column( 0 ), string.Empty );
		PanelText.Set( Column( 1 ), string.Empty );
		PanelText.Count( Column( 2 ), row.Count, "N0" );

		if ( row.DeltaCount != 0 )
			PanelText.Count( Column( 3 ), row.DeltaCount, "N0", prefix: row.DeltaCount > 0 ? "+" : "", suffix: "/s" );
		else
			PanelText.Set( Column( 3 ), string.Empty );

		SetBytesTone( 0 );
		SetCountTone( Tone( row.DeltaCount, LoudPerSecond ) );
	}

	private void BindReferrer( in MemoryRow row )
	{
		PanelText.Set( Column( 0 ), string.Empty );
		PanelText.Set( Column( 1 ), string.Empty );

		if ( row.Count > 0 ) PanelText.Count( Column( 2 ), row.Count, "N0", suffix: " refs" );
		else PanelText.Set( Column( 2 ), string.Empty );

		PanelText.Set( Column( 3 ), string.Empty );

		SetBytesTone( 0 );
		SetCountTone( 0 );
	}

	private void BindSection()
	{
		ClearColumns();

		SetBytesTone( 0 );
		SetCountTone( 0 );
	}

	//Growth is the point of this dock, so it is the loudest thing on it.
	private static int Tone( long delta, long loud ) => delta switch
	{
		var over when over >= loud => 3,
		> 0 => 2,
		< 0 => 1,
		_ => 0,
	};

	private void SetBytesTone( int tone )
	{
		if ( bytesTone == tone ) return;

		Column( 1 ).SetClass( ToneClass( bytesTone ), false );
		Column( 1 ).SetClass( ToneClass( tone ), true );

		bytesTone = tone;
	}

	private void SetCountTone( int tone )
	{
		if ( countTone == tone ) return;

		Column( 3 ).SetClass( ToneClass( countTone ), false );
		Column( 3 ).SetClass( ToneClass( tone ), true );

		countTone = tone;
	}

	private static string ToneClass( int tone ) => tone switch
	{
		3 => "alarm",
		2 => "up",
		1 => "down",
		_ => "flat",
	};
}
