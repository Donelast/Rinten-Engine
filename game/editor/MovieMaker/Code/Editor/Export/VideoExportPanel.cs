using Rinten.UI;
using System.Linq;

//Two types are called ControlSheet - the old widget grid in Editor, and the panel
//one. The enclosing namespace wins over a using, so the panel one is named here.
using ControlSheetPanel = Editor.Inspectors.ControlSheet;

namespace Editor.MovieMaker;

#nullable enable

//The window itself is still a window - the dock layer arranges those - and this is all of its content.
/// <summary>Everything inside the export window: the preview, and the settings beside it.</summary>
[StyleSheet( "VideoExportPanel.cs.scss" )]
public sealed class VideoExportPanel : Panel
{
	private readonly VideoExportPreviewPanel _preview;
	private readonly VideoExportTimelinePanel _timeline;
	private readonly Rinten.UI.Button _export;
	private readonly Rinten.UI.Button _cancel;

	public bool IsUpdating
	{
		get => _preview.IsUpdating;
		set => _preview.IsUpdating = value;
	}

	/// <summary>How big the preview is drawn, in pixels - no point rendering it bigger.</summary>
	public Vector2 PreviewSize => _preview.Box.Rect.Size;

	public VideoExportPanel( VideoExportWindow window, SerializedObject exportSerialized, SerializedObject configSerialized )
	{
		AddClass( "video-export" );

		var left = AddChild<Panel>( "left" );

		_preview = left.AddChild( new VideoExportPreviewPanel( window ) );
		_timeline = left.AddChild( new VideoExportTimelinePanel( window ) );

		var right = AddChild<Panel>( "right" );

		var settings = right.AddChild<ControlSheetPanel>( "settings" );

		settings.BuildProperties( exportSerialized
			.Concat( configSerialized )
			.Where( x => x.HasAttribute<FeatureAttribute>() )
			.ToList() );

		right.AddChild<Panel>( "spacer" );

		var summary = right.AddChild<ControlSheetPanel>( "summary" );

		summary.BuildProperties( exportSerialized
			.Where( x => x.HasAttribute<PropertyAttribute>() )
			.ToList() );

		var buttons = right.AddChild<Panel>( "buttons" );

		_export = buttons.AddChild( new Rinten.UI.Button( "Export", "ondemand_video", window.OnExportPressed ) );
		_export.AddClass( "primary" );

		_cancel = buttons.AddChild( new Rinten.UI.Button( "Cancel", "cancel_presentation", window.OnCancelPressed ) );
		_cancel.AddClass( "danger" );
		_cancel.AddClass( "hidden" );
	}

	/// <summary>Redraws the preview and the bar under it.</summary>
	public void Refresh()
	{
		_preview.Refresh();
		_timeline.Refresh();
	}

	/// <summary>Swaps the export button for the one that stops it, and back.</summary>
	public void SetExporting( bool exporting )
	{
		_export.SetClass( "hidden", exporting );
		_cancel.SetClass( "hidden", !exporting );
	}

	public void SetExportEnabled( bool enabled ) => _export.SetClass( "disabled", !enabled );
}
