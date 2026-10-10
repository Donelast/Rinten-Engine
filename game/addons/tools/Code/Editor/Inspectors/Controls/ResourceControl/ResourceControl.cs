using Editor.AssetPickers;

namespace Editor.Inspectors;

//This is the "from disk" half of editing a resource. What picks it for a property is ResourceWrapperControl, which owns
//the choice between a file, an embedded copy and a generator - so this one carries no editor attribute of its own.
[StyleSheet( "ResourceControl.cs.scss" )]
public class ResourceControl : BaseControl
{
	ResourceFieldPanel _field;
	ToolbarButton _soundPreview;
	ToolbarButton _openEditor;
	AssetType _assetType;
	ResourceDisplayState _display;
	int _displayHash;
	bool _pressPending;
	bool _dragStarted;
	Vector2 _pressStart;

	public override bool SupportsMultiEdit => true;

	protected virtual SerializedProperty EditingProperty => Property;

	bool IsInList => EditingProperty?.Parent is SerializedCollection;
	bool IsSoundType => _assetType == AssetType.SoundFile;

	public ResourceControl()
	{

		_field = AddChild<ResourceFieldPanel>();
		_field.AddEventListener( "onmousedown", OnFieldMouseDown );
		_field.AddEventListener( "onclick", OnFieldClick );

		_soundPreview = AddChild<ToolbarButton>( "sound-preview" );
		_soundPreview.AddClass( "sound-preview" );
		_soundPreview.Icon = "volume_up";
		_soundPreview.AddEventListener( "onmousedown", OnSoundPreviewMouseDown );
		//Hidden in code rather than by a class: this sheet has no rule for one, and the
		//button is a razor component that writes its own classes when it re-renders.
		_soundPreview.SetClass( "hidden", true );

		//The asset's own editor, one click away. It was only in the right-click menu, and a left click on the
		//field picks another asset - so a sprite, say, looked like something that could be chosen and never edited.
		_openEditor = AddChild<ToolbarButton>( "open-editor" );
		_openEditor.Icon = "edit";
		_openEditor.ToolTip = "Open in Editor";
		_openEditor.OnPressed = OpenInEditor;
		_openEditor.SetClass( "hidden", true );
	}

	public override bool AcceptEditorDrops => !IsReadOnly;

	public override DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		if ( ev.Data.Url?.Scheme == "https" )
		{
			SetClass( "drop-target", true );
			return DropAction.Link;
		}

		var asset = ResourceAssignment.AssetFrom( ev.Data );
		var accept = ResourceAssignment.CanAssign( EditingProperty, _assetType, asset );
		SetClass( "drop-target", accept );
		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnEditorDragLeave() => SetClass( "drop-target", false );

	public override void OnEditorDragDrop( Widget.DragEvent ev )
	{
		if ( Property is null || IsReadOnly ) return;

		if ( ev.Data.Url?.Scheme == "https" )
		{
			Property.Parent?.NoteStartEdit( EditingProperty );
			_ = DroppedUrl( ev.Data.Text );
			Property.Parent?.NoteFinishEdit( EditingProperty );
			ev.Action = DropAction.Link;
			return;
		}

		var asset = ResourceAssignment.AssetFrom( ev.Data );

		if ( !ResourceAssignment.CanAssign( EditingProperty, _assetType, asset ) )
		{
			Log.Warning( asset is null
				? $"ResourceControl drop: couldn't resolve an asset from the drag data (Text=\"{ev.Data.Text}\")"
				: $"ResourceControl drop: asset \"{asset.Path}\" ({asset.AssetType}) can't be assigned to {EditingProperty.PropertyType}" );
			return;
		}

		Property.Parent?.NoteStartEdit( EditingProperty );
		ResourceAssignment.Assign( EditingProperty, asset );
		Property.Parent?.NoteFinishEdit( EditingProperty );
		ev.Action = DropAction.Link;
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		_assetType = AssetType.FromType( Property.PropertyType );
		_displayHash = 0;
		SetClass( "has-sound-preview", IsSoundType );
		_soundPreview.SetClass( "hidden", !IsSoundType );
		RefreshDisplay( force: true );
	}

	public override void Tick()
	{
		UpdateDrag();

		if ( !EditorDrag.Active && HasClass( "drop-target" ) )
			SetClass( "drop-target", false );

		base.Tick();

		if ( Property is null ) return;
		RefreshDisplay();
	}

	protected override int BuildHash() =>
		HashCode.Combine(
			_display.Mode,
			_display.Title,
			_display.Path,
			IsReadOnly,
			MultipleRows,
			IsSoundType );

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		ResourceControlContextMenu.Fill( menu, EditingProperty, _display.Resource, _display.Asset, IsReadOnly );
	}

	public override string ToClipboardString() => ResourceControlContextMenu.ToClipboardString( _display.Resource );

	public override void FromClipboardString( string clipboard )
	{
		if ( IsReadOnly || Property is null ) return;

		ResourceControlContextMenu.Paste( EditingProperty, clipboard );
	}

	void RefreshDisplay( bool force = false )
	{
		if ( Property is null || _field is null ) return;

		var state = ResourceDisplay.Build( EditingProperty, _assetType );
		var hash = HashCode.Combine(
			state.Mode,
			state.Title,
			state.Path,
			state.Placeholder,
			state.Resource?.ResourcePath,
			MultipleRows );

		if ( !force && hash == _displayHash ) return;

		_displayHash = hash;
		_display = state;
		_field.Update( state );
		_openEditor.SetClass( "hidden", !CanOpenInEditor( state.Asset ) );
	}

	void UpdateDrag()
	{
		if ( !_pressPending || _dragStarted )
			return;

		if ( ( Application.MouseButtons & MouseButtons.Left ) == 0 )
		{
			_pressPending = false;
			return;
		}

		if ( Application.CursorPosition.Distance( _pressStart ) > 4 )
			TryStartDrag();
	}

	void OnFieldClick( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe || mpe.MouseButton != MouseButtons.Left )
			return;
		if ( IsReadOnly || _dragStarted )
			return;

		_pressPending = false;
		OpenPicker();
		e.StopPropagation();
	}

	void OnFieldMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe ) return;
		if ( mpe.MouseButton != MouseButtons.Left ) return;
		if ( IsReadOnly ) return;

		_pressPending = true;
		_dragStarted = false;
		_pressStart = Application.CursorPosition;
	}

	void TryStartDrag()
	{
		_dragStarted = true;
		_pressPending = false;

		var asset = _display.Asset;
		if ( asset is null )
			return;

		var path = asset.AbsolutePath.Replace( '\\', '/' );
		var data = new DragData
		{
			Object = asset,
			Text = $"file:///{path}",
			Url = new Uri( $"file:///{path}" )
		};
		EditorDrag.Begin( this, data, null );
	}

	void OnSoundPreviewMouseDown( PanelEvent e )
	{
		if ( IsReadOnly ) return;
		_pressPending = false;
		EditorUtility.PlayAssetSound( EditingProperty.GetValue<SoundFile>() );
		e.StopPropagation();
	}

	//The same test the menu's "Open in Editor" makes.
	static bool CanOpenInEditor( Asset asset )
		=> asset is { IsDeleted: false, IsProcedural: false } && IAssetEditor.HasEditorFor( asset );

	void OpenInEditor()
	{
		_pressPending = false;
		if ( CanOpenInEditor( _display.Asset ) ) _display.Asset.OpenInEditor();
	}

	void OpenPicker()
	{
		if ( Property is null || IsReadOnly ) return;

		try
		{
			AssetPicker.CloseOpenPickers();

			var resource = EditingProperty.GetValue<Resource>( null );
			var asset = resource is not null ? AssetSystem.FindByPath( resource.ResourcePath ) : null;
			var pickerAssetType = AssetType.FromType( resource.IsValid() ? resource.GetType() : Property.PropertyType );

			var options = new AssetPicker.PickerOptions { EnableMultiselect = IsInList, EnableMounts = false };
			var picker = AssetPicker.Create( PickerParent(), pickerAssetType, options );

			Property.Parent?.NoteStartEdit( EditingProperty );

			picker.Title = $"Select {Property.DisplayName}";
			picker.OnAssetPicked = assets =>
			{
				try
				{
					ResourceAssignment.AssignMany( EditingProperty, assets );
				}
				catch ( Exception ex )
				{
					Log.Error( ex, "ResourceControl: assigning the picked asset failed" );
				}
				finally
				{
					Property.Parent?.NoteFinishEdit( EditingProperty );
				}
			};

			picker.Show();
			picker.SetSelection( asset );
		}
		catch ( Exception ex )
		{
			Log.Error( ex, "ResourceControl: opening the asset picker failed" );
			try { Property.Parent?.NoteFinishEdit( EditingProperty ); }
			catch ( Exception finishEx ) { Log.Error( finishEx, "ResourceControl: NoteFinishEdit after a failed picker open" ); }
		}
	}

	static Widget PickerParent() => Rinten.Internal.GlobalToolsNamespace.EditorWindow;

	async Task DroppedUrl( string identUrl )
	{
		var asset = await AssetSystem.InstallAsync( identUrl );
		ResourceAssignment.Assign( EditingProperty, asset );
	}
}
