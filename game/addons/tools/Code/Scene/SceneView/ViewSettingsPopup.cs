using Editor.Inspectors;
using Rinten.UI;

namespace Editor;

/// <summary>Panel popup for per-viewport display and debug settings.</summary>
[StyleSheet( "ViewportToolPopup.scss" )]
public sealed class ViewSettingsPopup : Popup
{
	public static ViewSettingsPopup Open( Panel anchor, SceneViewportPanel viewport )
	{
		if ( anchor is null || !viewport.IsValid() ) return null;

		return new ViewSettingsPopup( anchor, viewport );
	}

	readonly SceneViewportPanel viewport;
	readonly List<DebugModeRow> debugRows = new();
	Inspectors.ControlSheet sheet;
	ViewportViewMode lastView;

	ViewSettingsPopup( Panel anchor, SceneViewportPanel viewport )
	{
		this.viewport = viewport;

		SetPositioning( anchor, PositionMode.BelowRight, 4 );
		AnchorPanel = anchor;
		CloseWhenParentIsHidden = true;
		AddClass( "editor-popup" );
		AddClass( "view-settings-popup" );
		AddClass( "inspector" );

		//The camera, the grid and the post processing of a game are its own; what the editor adds to it is how it is drawn.
		if ( !viewport.IsGameView )
		{
			var sheet = AddChild<Inspectors.ControlSheet>( "sheet" );
			this.sheet = sheet;
			sheet.Target = BuildProperties( viewport );
			sheet.Rebuild();
		}

		lastView = viewport.State.View;

		var body = AddChild<Panel>( "body" );

		BuildDebugModes( body );

		SyncDebugRows();
		SyncAnchor();
	}

	public override void Tick()
	{
		base.Tick();
		SyncDebugRows();
		SyncSheet();
	}

	void SyncSheet()
	{
		if ( sheet is null || !sheet.IsValid() || viewport is not { IsValid: true } ) return;

		var view = viewport.State.View;
		if ( view == lastView ) return;

		lastView = view;
		sheet.Target = BuildProperties( viewport );
		sheet.Rebuild();
	}

	static List<SerializedProperty> BuildProperties( SceneViewportPanel viewport )
	{
		var so = viewport.State.GetSerialized();
		var propertyNames = new List<string>
		{
			nameof( ViewportState.View ),
			nameof( ViewportState.WireframeMode ),
			nameof( ViewportState.EnablePostProcessing ),
		};

		if ( viewport.SceneView.Session.Scene is PrefabScene )
			propertyNames.Add( nameof( ViewportState.EnablePrefabLighting ) );

		propertyNames.Add( nameof( ViewportState.ShowGrid ) );
		propertyNames.Add( nameof( ViewportState.GridOpacity ) );

		if ( viewport.State.View == ViewportViewMode.Perspective )
			propertyNames.Add( nameof( ViewportState.GridAxis ) );

		var list = new List<SerializedProperty>();
		foreach ( var name in propertyNames )
		{
			var property = so.GetProperty( name );
			if ( property is not null ) list.Add( property );
		}

		return list;
	}

	void BuildDebugModes( Panel body )
	{
		if ( !viewport.IsGameView )
			body.AddChild<Panel>( "separator" ).AddClass( "separator" );

		var described = EditorTypeLibrary.GetEnumDescription( typeof( SceneCameraDebugMode ) )
			.ToDictionary( x => (SceneCameraDebugMode)x.ObjectValue );

		string heading = null;

		foreach ( var (group, mode) in Rinten.Rendering.DebugViews.Working )
		{
			if ( group is not null && group != heading )
			{
				heading = group;
				var headingLabel = body.AddChild<Rinten.UI.Label>( "heading" );
				headingLabel.AddClass( "heading" );
				headingLabel.Text = group;
			}

			if ( !described.TryGetValue( mode, out var entry ) ) continue;

			debugRows.Add( new DebugModeRow( body, viewport, mode, entry.Title, entry.Icon ) );
		}
	}

	void SyncDebugRows()
	{
		foreach ( var row in debugRows )
			row.Sync();
	}

	sealed class DebugModeRow
	{
		readonly SceneViewportPanel viewport;
		readonly SceneCameraDebugMode mode;
		readonly IconPanel check;

		public DebugModeRow( Panel parent, SceneViewportPanel viewport, SceneCameraDebugMode mode, string title, string icon )
		{
			this.viewport = viewport;
			this.mode = mode;

			var row = parent.AddChild<Panel>( "menu-row" );
			row.AddClass( "menu-row" );

			if ( !string.IsNullOrEmpty( icon ) )
				row.AddChild<IconPanel>( "icon" ).Text = icon;

			row.AddChild<Rinten.UI.Label>( "caption" ).Text = title;

			check = row.AddChild<IconPanel>( "check" );
			check.Text = "check";

			row.AddEventListener( "onmousedown", _ => viewport.State.RenderMode = mode );
		}

		public void Sync() => check.Text = viewport.State.RenderMode == mode ? "check" : "";
	}
}
