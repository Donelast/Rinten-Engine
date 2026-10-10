namespace Editor;

//One line of the profiler: a span of the frame, a GPU zone, or a heading.
internal sealed class ProfilerRowPanel : PanelListRow
{
	//How many number columns a line has: time, worst, self, calls, allocation.
	private const int Columns = 5;

	//How wide the bar's own scale is, in milliseconds.
	private const float BarScaleMs = 8.0f;

	private int heat = -1;

	public ProfilerRowPanel() : base( Columns )
	{
		AddClass( "profiler-row" );

		//The two the sheet colours by hand, named rather than counted: a pseudo-class selector would put every row's position into the cascade.
		Column( 0 ).AddClass( "time" );
		Column( Columns - 1 ).AddClass( "alloc" );
	}

	/// <summary>Puts this row over a line: everything about it that is not a number.</summary>
	public void Bind( in ProfilerRow row, bool folded, int band = 0 )
	{
		SetBand( band );
		SetSection( row.Kind == ProfilerRowKind.Section );
		SetClass( "top", row.Kind == ProfilerRowKind.Cpu && row.Depth == 0 );
		SetDepth( row.Depth );
		SetChevron( row.Foldable, folded );
		SetName( row.Kind == ProfilerRowKind.Cpu ? row.Entry.Name : row.Name );

		Refresh( row );
	}

	/// <summary>The same line, with the numbers it carries now.</summary>
	public void Refresh( in ProfilerRow row )
	{
		switch ( row.Kind )
		{
			case ProfilerRowKind.Cpu: RefreshCpu( row.Entry ); break;
			case ProfilerRowKind.Gpu: RefreshGpu( row ); break;
			default: RefreshSection(); break;
		}
	}

	/// <summary>Drops what this row was holding while it waits out of the window.</summary>
	public void Unbind()
	{
		Index = -1;

		SetName( string.Empty );
	}

	private void RefreshCpu( Rinten.Diagnostics.FrameProfiler.Entry entry )
	{
		PanelText.Number( Column( 0 ), entry.AvgMs, "F2", " ms" );
		PanelText.Number( Column( 1 ), entry.MaxMs, "F2" );

		if ( entry.HasChildren ) PanelText.Number( Column( 2 ), entry.SelfMs, "F2" );
		else PanelText.Set( Column( 2 ), string.Empty );

		if ( entry.Calls > 1 ) PanelText.Count( Column( 3 ), entry.Calls, prefix: "×" );
		else PanelText.Set( Column( 3 ), string.Empty );

		PanelText.Bytes( Column( 4 ), entry.AllocBytes );

		SetClass( "loud", entry.AllocBytes > 16 * 1024 );
		SetHeat( entry.AvgMs );
		SetBar( entry.AvgMs / BarScaleMs );
	}

	private void RefreshGpu( in ProfilerRow row )
	{
		PanelText.Number( Column( 0 ), row.GpuMs, "F2", " ms" );
		PanelText.Number( Column( 1 ), row.CpuMs, "F2", " cpu" );

		ClearColumns( 2 );

		SetClass( "loud", false );
		SetHeat( row.GpuMs );
		SetBar( row.GpuMs / BarScaleMs );
	}

	private void RefreshSection()
	{
		ClearColumns();

		SetClass( "loud", false );
		SetHeat( 0.0f );
		SetBar( 0.0f );
	}

	//Which band of cost the row is in. A class rather than a colour, so an unchanged row does not re-cascade.
	private void SetHeat( float ms )
	{
		var band = ms switch
		{
			> 8.0f => 3,
			> 2.0f => 2,
			> 0.5f => 1,
			_ => 0,
		};

		if ( heat == band ) return;

		SetClass( HeatClass( heat ), false );
		SetClass( HeatClass( band ), true );

		heat = band;
	}

	private static string HeatClass( int band ) => band switch
	{
		3 => "hot",
		2 => "warm",
		1 => "cool",
		_ => "idle",
	};
}
