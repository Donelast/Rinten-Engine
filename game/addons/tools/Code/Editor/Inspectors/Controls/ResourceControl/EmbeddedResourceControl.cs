namespace Editor.Inspectors;

using Editor.AssetPickers;
using Editor.TreeBrowser;
using Rinten.UI;
using System.Text.Json.Nodes;

/// <summary>A resource kept inside whatever uses it rather than in a file, edited in a popup of its own properties.</summary>
[StyleSheet( "EmbeddedResourceControl.cs.scss" )]
public sealed class EmbeddedResourceControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	readonly Panel _field;
	readonly Rinten.UI.Label _value;

	EmbeddedResourcePopup _popup;
	AssetType _assetType;

	Resource Resource => Property?.GetValue<Resource>( null );

	public EmbeddedResourceControl()
	{
		ShowBackground = false;

		Style.Cursor = "pointer";

		_field = AddChild<Panel>( "field" );

		_value = _field.AddChild<Rinten.UI.Label>( "value" );
		_value.Tokenize = false;

		_field.AddChild<IconPanel>( "indicator" ).Text = "edit_note";
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		//The popup edits the resource's own properties, so there has to be one to edit.
		EmbeddedResources.EnsureResource( Property );

		_assetType = AssetType.FromType( Resource?.GetType() ?? Property.PropertyType );

		SyncDisplay();
	}

	public override void Tick()
	{
		base.Tick();

		if ( _popup is not null && !_popup.IsValid() )
			_popup = null;

		SetClass( "open", _popup is not null );
		SyncDisplay();
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( Property is null || IsReadOnly ) return;
		if ( e.MouseButton != MouseButtons.Left ) return;

		OpenEditor();
		e.StopPropagation();
	}

	void SyncDisplay()
	{
		if ( Property is null || !_value.IsValid() ) return;

		if ( Property.IsMultipleDifferentValues )
		{
			_value.SetClass( "multiple", true );
			_value.Text = "Multiple Values";
			return;
		}

		_value.SetClass( "multiple", false );

		var name = _assetType?.FriendlyName ?? DisplayInfo.ForType( Property.PropertyType ).Name;

		_value.Text = $"Embedded {name}";
	}

	void OpenEditor()
	{
		if ( _popup is { IsValid: true } )
		{
			_popup.Delete();
			_popup = null;
			return;
		}

		_popup = EmbeddedResourcePopup.Open( this, Property, OnResourceReplaced );
	}

	//The popup swapped the resource for another, so the field has to say so.
	void OnResourceReplaced()
	{
		_assetType = AssetType.FromType( Resource?.GetType() ?? Property.PropertyType );
		SyncDisplay();
	}
}

//The embedded resource's own properties, and the handful of things you can do to it.
[StyleSheet( "EmbeddedResourceControl.cs.scss" )]
sealed class EmbeddedResourcePopup : Popup
{
	SerializedProperty _property;
	AssetType _assetType;
	Action _changed;

	Panel _toolbar;
	ControlSheet _sheet;

	public static EmbeddedResourcePopup Open( Panel source, SerializedProperty property, Action changed )
	{
		CloseAll();

		var popup = new EmbeddedResourcePopup();

		popup.AddClass( "embedded-resource-popup" );
		popup.SetPositioning( source, PositionMode.BelowStretch, 2 );
		popup.CloseWhenParentIsHidden = true;

		popup._property = property;
		popup._changed = changed;
		popup.Build();
		popup.SyncAnchor();

		return popup;
	}

	Resource Resource => _property?.GetValue<Resource>( null );

	bool IsReadOnly => _property is null || !_property.IsEditable;

	void Build()
	{
		DeleteChildren();

		if ( _property is null ) return;

		var resource = Resource;

		_assetType = AssetType.FromType( resource?.GetType() ?? _property.PropertyType );

		_toolbar = AddChild<Panel>( "toolbar" );
		_sheet = AddChild<ControlSheet>();

		var serialized = resource?.GetSerialized();

		if ( EmbeddedResources.SupportedTypes( _property, resource ).Count > 0 )
			AddAction( "Type", "type_specimen", OpenTypeMenu );

		if ( serialized.IsValid() )
		{
			if ( _assetType?.HasEditor ?? false )
				AddAction( "Open in Editor", "dvr", OpenInEditor );

			AddAction( "Save As", "drive_file_move", () => _ = ConvertToFile() );
			AddAction( "Load", "swap_horiz", LoadFromFile );
			AddAction( "Copy", "content_copy", Copy, alwaysEnabled: true );
			AddAction( "Paste", "content_paste", Paste );
			AddAction( "Clear", "delete", Clear );

			_sheet.Target = serialized;
			_sheet.Rebuild();
		}
	}

	void AddAction( string tooltip, string icon, Action action, bool alwaysEnabled = false )
	{
		var button = _toolbar.AddChild<ToolbarButton>();

		button.Icon = icon;
		button.ToolTip = tooltip;
		button.Disabled = IsReadOnly && !alwaysEnabled;
		button.OnPressed = action;
	}

	//Rebuilds after something replaced the resource, and tells the field about it.
	void Replaced()
	{
		Build();
		_changed?.Invoke();
	}

	void OpenTypeMenu()
	{
		var resource = Resource;
		var menu = new Menu();

		foreach ( var type in EmbeddedResources.SupportedTypes( _property, resource ) )
		{
			var picked = type;

			menu.AddOption( picked.Title, picked.Icon ?? "category", () =>
			{
				_property.Parent?.NoteStartEdit( _property );
				EmbeddedResources.SetType( _property, picked );
				_property.Parent?.NoteFinishEdit( _property );

				Replaced();
			} );
		}

		//At the pointer: the toolbar is the whole row, so its left edge is nowhere near the button pressed.
		menu.OpenAtCursor( _toolbar );
	}

	void Clear()
	{
		_property.Parent?.NoteStartEdit( _property );
		EmbeddedResources.Reset( _property );
		_property.Parent?.NoteFinishEdit( _property );

		Replaced();
	}

	void Copy()
	{
		if ( Resource is not GameResource resource ) return;

		EditorUtility.Clipboard.Copy( resource.Serialize().ToJsonString() );
	}

	void Paste()
	{
		if ( Resource is not GameResource resource ) return;

		var text = EditorUtility.Clipboard.Paste();
		if ( string.IsNullOrWhiteSpace( text ) ) return;

		JsonObject parsed;

		try
		{
			parsed = JsonNode.Parse( text ) as JsonObject;
		}
		catch ( Exception )
		{
			//Whatever is on the clipboard isn't a resource.
			return;
		}

		if ( parsed is null ) return;

		_property.Parent?.NoteStartEdit( _property );
		resource.Deserialize( parsed );
		EmbeddedResources.MarkEmbedded( _property, resource );
		_property.SetValue( resource );
		_property.Parent?.NoteFinishEdit( _property );

		Replaced();
	}

	//Opens the embedded resource in the editor its type has.
	void OpenInEditor()
	{
		IAssetEditor.OpenInEditor( AssetSystem.CreateEmbeddedAsset( _property ), out _ );

		Delete();
	}

	//Picks a file to embed, keeping everything that was on it.
	void LoadFromFile()
	{
		AssetPicker.CloseOpenPickers();

		var picker = AssetPicker.Create( Rinten.Internal.GlobalToolsNamespace.EditorWindow, _assetType,
			new AssetPicker.PickerOptions { EnableMultiselect = false, EnableMounts = false } );

		picker.Title = $"Select {_property.DisplayName}";
		picker.OnAssetPicked = assets =>
		{
			if ( assets?.FirstOrDefault() is not { } asset ) return;
			if ( !asset.TryLoadResource<GameResource>( out var loaded ) ) return;

			_property.Parent?.NoteStartEdit( _property );
			EmbeddedResources.CreateFromFile( _property, loaded );
			_property.Parent?.NoteFinishEdit( _property );

			Replaced();
		};

		picker.Show();
	}

	//Writes the embedded resource out as a file, and points the property at that file.
	async Task ConvertToFile()
	{
		if ( _assetType is null ) return;

		var dialog = new FileDialog( null )
		{
			Title = "Convert embedded resource to file..",
			DefaultSuffix = _assetType.FileExtension,
			Directory = Project.Current.GetAssetsPath()
		};

		dialog.SelectFile( $"resource.{_assetType.FileExtension}" );
		dialog.SetFindFile();
		dialog.SetModeSave();
		dialog.SetNameFilter( $"Resource  (*.{_assetType.FileExtension})" );

		if ( !dialog.Execute() ) return;

		var asset = AssetSystem.CreateResource( _assetType.FileExtension, dialog.SelectedFile );
		await asset.CompileIfNeededAsync();

		if ( !asset.TryLoadResource<GameResource>( out var saved ) ) return;

		saved.CopyFrom( _property.GetValue<GameResource>() );

		//It lives in a file now, so it is not embedded any more.
		saved.EmbeddedResource = null;
		asset.SaveToDisk( saved );

		_property.Parent?.NoteStartEdit( _property );
		_property.SetValue( saved );
		_property.Parent?.NoteFinishEdit( _property );

		TreeAssetBrowser.RefreshOpen();

		_changed?.Invoke();
		Delete();
	}
}
