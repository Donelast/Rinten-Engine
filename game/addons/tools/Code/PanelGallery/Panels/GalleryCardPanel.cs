using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The card owns nothing but the probes it makes; each probe owns one copy of the example. Options changing re-dress the stage in place, except the number of copies, which is a rebuild.
/// <summary>One example on the page: its name and description, the example itself on a stage, and a line of readings under it.</summary>
[StyleSheet( "GalleryCardPanel.cs.scss" )]
public sealed class GalleryCardPanel : Panel, IGalleryReadout
{
	public GalleryEntry Entry { get; }

	/// <summary>Told when the card is clicked anywhere that the example did not take the click.</summary>
	public Action Pressed { get; set; }

	readonly GalleryViewOptions options;
	readonly Panel stage;
	readonly Label readout;
	readonly List<GalleryProbePanel> probes = new();

	int builtCopies;
	RealTimeSince sinceReadout;

	public GalleryCardPanel( GalleryEntry entry, GalleryViewOptions options )
	{
		Entry = entry;
		this.options = options;

		AddClass( "gallery-card" );

		var head = AddChild<Panel>( "head" );

		var icon = head.AddChild<Label>( "icon material-icon" );
		icon.Text = entry.Icon;

		var title = head.AddChild<Label>( "title" );
		title.Text = entry.Title;

		if ( entry.IsLegacyWidget ) Badge( head, "widget", "legacy" );
		if ( entry.RequiresWindow ) Badge( head, "needs window", "window" );

		var id = head.AddChild<Label>( "id micro" );
		id.Text = entry.Id;

		if ( !string.IsNullOrEmpty( entry.Description ) )
		{
			var description = AddChild<Label>( "description" );
			description.Text = entry.Description;
		}

		stage = AddChild<Panel>( "stage" );
		readout = AddChild<Label>( "readout micro" );

		AddEventListener( "onclick", _ => Pressed?.Invoke() );

		options.Changed += OnOptionsChanged;

		Build();
		ApplyOptions();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		options.Changed -= OnOptionsChanged;
	}

	static void Badge( Panel head, string text, string kind )
	{
		var badge = head.AddChild<Label>( "badge micro" );
		badge.AddClass( kind );
		badge.Text = text;
	}

	void Build()
	{
		stage.DeleteChildren( true );
		probes.Clear();

		builtCopies = options.Copies;

		for ( var i = 0; i < builtCopies; i++ )
		{
			var probe = new GalleryProbePanel( Entry );

			stage.AddChild( probe );
			probes.Add( probe );
		}

		sinceReadout = 10;
	}

	void ApplyOptions()
	{
		foreach ( var cls in GalleryViewOptions.AllWidthClasses )
			stage.SetClass( cls, false );

		stage.AddClass( options.WidthClass );

		foreach ( var probe in probes )
			probe.SetReadOnly( options.ReadOnly );
	}

	void OnOptionsChanged()
	{
		if ( builtCopies != options.Copies )
			Build();

		ApplyOptions();
	}

	public void SetSelected( bool selected ) => SetClass( "is-selected", selected );

	public override void Tick()
	{
		base.Tick();

		if ( sinceReadout < 0.5f ) return;

		sinceReadout = 0;
		readout.Text = Summary();
	}

	string Summary()
	{
		if ( Error is { } error )
			return $"failed: {error}";

		var copies = builtCopies == 1 ? "" : $"{builtCopies} copies · ";
		var frame = WindowCost.IsMeasured
			? $" · window frame {WindowCost.TotalMs:F2} ms (layout {WindowCost.LayoutMs:F2}, input {WindowCost.InputMs:F2}, draw {WindowCost.DrawMs:F2}) at {WindowCost.FramesPerSecond:F0} fps"
			: "";

		return $"{copies}{PanelCount} panels · built in {BuildMilliseconds:F1} ms · {TicksPerSecond:F0} ticks/s{frame}";
	}

	public int PanelCount => probes.Sum( x => x.PanelCount );
	public double BuildMilliseconds => probes.Sum( x => x.BuildMilliseconds );
	public float TicksPerSecond => probes.Count > 0 ? probes[0].TicksPerSecond : 0;
	public string Error => probes.FirstOrDefault( x => x.Error is not null )?.Error?.Message;

	/// <summary>What the whole window spends per frame with this card in it - the window's, not the card's own share.</summary>
	public UiFrameCost WindowCost => FindRootPanel()?.FrameCost ?? default;
}
