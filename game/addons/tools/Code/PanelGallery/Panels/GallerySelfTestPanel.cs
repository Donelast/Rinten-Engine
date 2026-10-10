using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

/// <summary>The dock along the bottom: builds every example once and lists what could not be built.</summary>
[StyleSheet( "GallerySelfTestPanel.cs.scss" )]
public sealed class GallerySelfTestPanel : Panel
{
	readonly GallerySession session;
	readonly Label summary;
	readonly Panel rows;

	public GallerySelfTestPanel( GallerySession session )
	{
		this.session = session;

		AddClass( "gallery-selftest" );

		var bar = AddChild<Panel>( "bar" );

		var run = bar.AddChild<TextButtonPanel>();
		run.Text = "Run self-test";
		run.Icon = "checklist";
		run.Pressed = Run;

		summary = bar.AddChild<Label>( "summary" );
		summary.Text = "Builds every example with nothing on screen and lists the ones that throw.";

		rows = AddChild<Panel>( "rows" );
	}

	void Run()
	{
		var results = GallerySelfTest.Run( session.Catalog.Entries() );

		var passed = results.Count( x => x.Passed && !x.Skipped );
		var skipped = results.Count( x => x.Skipped );
		var failed = results.Count( x => !x.Passed );

		summary.Text = $"{passed} built, {failed} failed, {skipped} need a window, {results.Count} examples";
		summary.SetClass( "bad", failed > 0 );

		rows.DeleteChildren( true );

		//Failures first, then the rest in catalog order - the point of the list is what went wrong.
		foreach ( var result in results.OrderBy( x => x.Passed ? 1 : 0 ) )
			rows.AddChild( new GallerySelfTestRowPanel( result, () => session.Show( result.Entry.Id ) ) );
	}
}

//One line of the self-test: what was built, whether it built, and how long it took.
sealed class GallerySelfTestRowPanel : Panel
{
	public GallerySelfTestRowPanel( GallerySelfTestResult result, Action pressed )
	{
		AddClass( "list-row" );
		AddClass( "pressable" );
		SetClass( "failed", !result.Passed );
		SetClass( "skipped", result.Skipped );

		var icon = AddChild<Label>( "icon material-icon" );
		icon.Text = !result.Passed ? "close" : result.Skipped ? "remove" : "check";

		var id = AddChild<Label>( "id" );
		id.Text = result.Entry.Id;

		var message = AddChild<Label>( "message" );
		message.Text = result.Message;

		var time = AddChild<Label>( "time micro" );
		time.Text = result.Skipped ? "" : $"{result.PanelCount} panels · {result.Milliseconds:F1} ms";

		AddEventListener( "onclick", _ => pressed?.Invoke() );
	}
}
