using Editor.Inspectors;
using Rinten.UI;
using System.Diagnostics;

namespace Editor.Gallery;

/// <summary>One example instance with build/tick/size stats; a throwing example shows a GalleryErrorPanel</summary>
public sealed class GalleryProbePanel : Panel
{
	public GalleryEntry Entry { get; }

	/// <summary>The example, or null where it could not be built.</summary>
	public Panel Example { get; }

	public Exception Error { get; }
	public double BuildMilliseconds { get; }

	/// <summary>How often this subtree was ticked over the last second.</summary>
	public float TicksPerSecond { get; private set; }

	int ticks;
	RealTimeSince sinceSample;

	public GalleryProbePanel( GalleryEntry entry )
	{
		Entry = entry;

		AddClass( "gallery-probe" );

		var watch = Stopwatch.StartNew();

		try
		{
			Example = entry.Create() ?? throw new InvalidOperationException( "the example's factory returned null" );

			AddChild( Example );
		}
		catch ( Exception e )
		{
			Error = e is System.Reflection.TargetInvocationException { InnerException: { } cause } ? cause : e;

			AddChild( new GalleryErrorPanel( Error ) );
		}

		BuildMilliseconds = watch.Elapsed.TotalMilliseconds;
	}

	public override void Tick()
	{
		base.Tick();

		ticks++;

		if ( sinceSample < 1.0f ) return;

		TicksPerSecond = ticks / (float)sinceSample;
		ticks = 0;
		sinceSample = 0;
	}

	/// <summary>Panels under the example, itself included.</summary>
	public int PanelCount => Example is null ? 0 : GallerySelfTest.CountPanels( Example );

	/// <summary>Every property editor in the example put into, or out of, its read-only state.</summary>
	public void SetReadOnly( bool readOnly )
	{
		if ( Example is null ) return;

		Visit( Example, panel =>
		{
			if ( panel is BaseControl control )
				control.ReadOnly = readOnly;
		} );
	}

	static void Visit( Panel panel, Action<Panel> visit )
	{
		visit( panel );

		foreach ( var child in panel.Children.ToArray() )
			Visit( child, visit );
	}
}
