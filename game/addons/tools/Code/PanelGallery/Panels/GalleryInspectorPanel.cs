using Rinten.UI;
using PanelSheet = Editor.Inspectors.ControlSheet;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The options are a PanelSheet over GalleryViewOptions - the gallery's own sheet stack editing the gallery, which is the cheapest test of that stack there is.
/// <summary>The dock on the right: the view options every card obeys, and the readings off the card picked.</summary>
[StyleSheet( "GalleryInspectorPanel.cs.scss" )]
public sealed class GalleryInspectorPanel : Panel
{
	readonly GallerySession session;

	readonly Label name;
	readonly Label id;
	readonly Label kind;
	readonly Label panels;
	readonly Label build;
	readonly Label ticks;
	readonly Label error;

	readonly Label frameTotal;
	readonly Label frameLayout;
	readonly Label frameInput;
	readonly Label frameDraw;
	readonly Label frameRate;

	RealTimeSince sinceRefresh;

	public GalleryInspectorPanel( GallerySession session )
	{
		this.session = session;

		AddClass( "gallery-inspector" );

		Heading( "View" );

		var sheet = AddChild<PanelSheet>();
		sheet.AddProperty( session.Options, x => x.Width );
		sheet.AddProperty( session.Options, x => x.ReadOnly );
		sheet.AddProperty( session.Options, x => x.Copies );

		var actions = AddChild<Panel>( "row gap-2 actions" );

		var rebuild = actions.AddChild<TextButtonPanel>();
		rebuild.Text = "Rebuild page";
		rebuild.Icon = "autorenew";
		rebuild.Pressed = session.RequestPageRebuild;

		var reload = actions.AddChild<TextButtonPanel>();
		reload.Text = "Rebuild catalog";
		reload.Icon = "refresh";
		reload.Pressed = session.Reload;

		Heading( "Selected" );

		var table = AddChild<Panel>( "readings" );

		name = Reading( table, "Example" );
		id = Reading( table, "Id" );
		kind = Reading( table, "Kind" );
		panels = Reading( table, "Panels" );
		build = Reading( table, "Built in" );
		ticks = Reading( table, "Ticks" );
		error = Reading( table, "Error" );

		//The window's own frame, whichever card is picked: what ui_perf prints, live. The gallery
		//is a tab of the main window, so this is the editor's frame with the page open in it.
		Heading( "Window frame" );

		var frame = AddChild<Panel>( "readings" );

		frameTotal = Reading( frame, "Total" );
		frameLayout = Reading( frame, "Layout" );
		frameInput = Reading( frame, "Input" );
		frameDraw = Reading( frame, "Draw" );
		frameRate = Reading( frame, "Frames" );

		Refresh();
	}

	void Heading( string text )
	{
		var heading = AddChild<Label>( "heading micro" );
		heading.Text = text;
	}

	static Label Reading( Panel table, string title )
	{
		var row = table.AddChild<Panel>( "reading" );

		var key = row.AddChild<Label>( "key" );
		key.Text = title;

		return row.AddChild<Label>( "value" );
	}

	public override void Tick()
	{
		base.Tick();

		if ( sinceRefresh < 0.25f ) return;

		sinceRefresh = 0;
		Refresh();
	}

	void Refresh()
	{
		RefreshFrame();

		var entry = session.SelectedEntry;

		if ( entry is null )
		{
			name.Text = "Click a card";
			id.Text = "";
			kind.Text = "";
			panels.Text = "";
			build.Text = "";
			ticks.Text = "";
			error.Text = "";
			return;
		}

		name.Text = entry.Title;
		id.Text = entry.Id;
		kind.Text = entry.IsLegacyWidget ? "widget in a panel" : entry.RequiresWindow ? "panel, needs a window" : "panel";

		var readout = session.SelectedReadout;

		panels.Text = readout is null ? "-" : readout.PanelCount.ToString();
		build.Text = readout is null ? "-" : $"{readout.BuildMilliseconds:F1} ms";
		ticks.Text = readout is null ? "-" : $"{readout.TicksPerSecond:F0} / s";
		error.Text = readout?.Error ?? "none";

		error.SetClass( "bad", readout?.Error is not null );
	}

	void RefreshFrame()
	{
		var cost = FindRootPanel()?.FrameCost ?? default;

		if ( !cost.IsMeasured )
		{
			frameTotal.Text = "not measured";
			frameLayout.Text = "";
			frameInput.Text = "";
			frameDraw.Text = "";
			frameRate.Text = "";
			return;
		}

		frameTotal.Text = $"{cost.TotalMs:F2} ms";
		frameLayout.Text = $"{cost.LayoutMs:F2} ms";
		frameInput.Text = $"{cost.InputMs:F2} ms";
		frameDraw.Text = $"{cost.DrawMs:F2} ms";
		frameRate.Text = $"{cost.FramesPerSecond:F0} / s";
	}
}
