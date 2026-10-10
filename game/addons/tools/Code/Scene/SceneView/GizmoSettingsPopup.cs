using Rinten.UI;

namespace Editor;

/// <summary>Popup to toggle which component gizmos are drawn in the scene view.</summary>
[StyleSheet( "GizmoSettingsPopup.cs.scss" )]
public sealed class GizmoSettingsPopup : Popup
{
	public static GizmoSettingsPopup Open( Panel anchor )
	{
		if ( anchor is null ) return null;

		return new GizmoSettingsPopup( anchor );
	}

	readonly List<GizmoCategorySection> categories = new();

	GizmoSettingsPopup( Panel anchor )
	{
		SetPositioning( anchor, PositionMode.BelowRight, 4 );
		AnchorPanel = anchor;
		CloseWhenParentIsHidden = true;
		AddClass( "editor-popup" );
		AddClass( "gizmo-settings-popup" );

		var groups = EditorTypeLibrary.GetTypes()
			.Where( x => x.TargetType.IsAssignableTo( typeof( Component ) ) )
			.Where( x => x.GetAttribute<EditorHandleAttribute>() != null )
			.OrderBy( x => x.Title )
			.GroupBy( x => x.Group ?? "Other" )
			.OrderBy( x => x.Key )
			.ToList();

		var body = AddChild<Panel>( "body" );

		for ( var i = 0; i < groups.Count; i++ )
		{
			var section = new GizmoCategorySection( body, groups[i].Key, groups[i].ToList() );
			section.CategoryChanged = UpdateCategoryCheckbox;
			categories.Add( section );

			if ( i != groups.Count - 1 )
				body.AddChild<Panel>( "separator" ).AddClass( "separator" );
		}

		foreach ( var category in categories )
			category.SyncHeaderVisual();

		SyncAnchor();
	}

	void UpdateCategoryCheckbox( GizmoCategorySection category ) => category.SyncHeaderVisual();

	sealed class GizmoCategorySection
	{
		readonly Panel header;
		readonly IconPanel categoryCheck;

		public List<GizmoSettingsRow> Rows { get; } = new();
		public Action<GizmoCategorySection> CategoryChanged { get; set; }

		public GizmoCategorySection( Panel parent, string title, List<TypeDescription> types )
		{
			var block = parent.AddChild<Panel>( "category" );

			header = block.AddChild<Panel>( "header" );

			header.AddChild<Rinten.UI.Label>( "title" ).Text = title;
			header.AddChild<Panel>( "stretch" );

			categoryCheck = header.AddChild<IconPanel>( "category-check" );
			categoryCheck.Text = "check";

			header.AddEventListener( "onmousedown", _ => OnCategoryToggled() );

			block.AddChild<Panel>( "rule" ).AddClass( "rule" );

			foreach ( var type in types )
			{
				var row = new GizmoSettingsRow( block, type );
				row.Changed = () => CategoryChanged?.Invoke( this );
				Rows.Add( row );
			}
		}

		public void SyncHeaderVisual()
		{
			var enabledCount = Rows.Count( r => r.IsEnabled );

			if ( enabledCount == 0 )
			{
				categoryCheck.SetClass( "partial", false );
				categoryCheck.Text = "";
			}
			else if ( enabledCount == Rows.Count )
			{
				categoryCheck.SetClass( "partial", false );
				categoryCheck.Text = "check";
			}
			else
			{
				categoryCheck.SetClass( "partial", true );
				categoryCheck.Text = "remove";
			}
		}

		void OnCategoryToggled()
		{
			var allEnabled = Rows.All( r => r.IsEnabled );
			var enable = !allEnabled;

			foreach ( var row in Rows )
				row.SetEnabled( enable );

			SyncHeaderVisual();
			CategoryChanged?.Invoke( this );
		}
	}

	sealed class GizmoSettingsRow
	{
		readonly TypeDescription type;
		readonly IconPanel check;

		public bool IsEnabled => EditorScene.GizmoSettings.IsGizmoEnabled( type.TargetType );
		public Action Changed { get; set; }

		public GizmoSettingsRow( Panel parent, TypeDescription type )
		{
			this.type = type;

			var row = parent.AddChild<Panel>( "row" );
			row.AddClass( "gizmo-row" );

			row.AddChild<Rinten.UI.Label>( "label" ).Text = type.Title;

			AddIcon( row, type );

			check = row.AddChild<IconPanel>( "check" );
			check.Text = "check";
			SyncVisual();

			row.AddEventListener( "onmousedown", _ => Toggle() );
		}

		void Toggle()
		{
			SetEnabled( !IsEnabled );
			Changed?.Invoke();
		}

		void SyncVisual()
		{
			check.Text = IsEnabled ? "check" : "";
		}

		static void AddIcon( Panel row, TypeDescription type )
		{
			var handleAttr = type.GetAttribute<EditorHandleAttribute>();

			var iconHost = row.AddChild<Panel>( "gizmo-icon" );

			if ( handleAttr?.Texture is not null )
			{
				var texture = Texture.Load( handleAttr.Texture, warnOnMissing: false );
				if ( texture is { IsValid: true } )
				{
					iconHost.Style.BackgroundImage = texture;
					return;
				}
			}

			var iconName = handleAttr?.Icon ?? type.Icon;
			if ( string.IsNullOrEmpty( iconName ) )
			{
				iconHost.Delete();
				return;
			}

			var icon = iconHost.AddChild<IconPanel>( "material-icon" );
			icon.Text = iconName;
		}

		public void SetEnabled( bool enabled )
		{
			EditorScene.GizmoSettings.SetGizmoEnabled( type.TargetType, enabled );
			SyncVisual();
		}
	}
}
