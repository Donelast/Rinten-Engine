namespace Editor.Inspectors;

using Editor.TreeBrowser;
using Rinten.UI;

/// <summary>An image or texture asset, stored on the property as its path.</summary>
[StyleSheet( "ResourceControl/ResourceControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( TextureImagePathAttribute )] )]
public sealed class TextureImageControl : BaseControl
{
	const string PickerName = "Texture Image";

	readonly ResourceFieldPanel _field;

	ResourceDisplayState _display;
	int _displayHash;
	bool _pressPending;
	bool _dragStarted;
	Vector2 _pressStart;

	public override bool SupportsMultiEdit => true;

	public TextureImageControl()
	{

		_field = AddChild<ResourceFieldPanel>();
		_field.AddEventListener( "onmousedown", OnFieldMouseDown );
		_field.AddEventListener( "onclick", OnFieldClick );
	}

	public override void Rebuild()
	{
		_displayHash = 0;
		RefreshDisplay();
	}

	public override void Tick()
	{
		UpdateDrag();

		if ( !EditorDrag.Active && HasClass( "drop-target" ) )
			SetClass( "drop-target", false );

		base.Tick();

		RefreshDisplay();
	}

	protected override int BuildHash() =>
		HashCode.Combine( _display.Mode, _display.Title, _display.Path, IsReadOnly, MultipleRows );

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		var path = CurrentPath();
		var asset = CurrentAsset();

		menu.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() )
			.Enabled = asset is not null && !asset.IsProcedural;

		menu.AddOption( "Find in Asset Browser", "search", () => TreeAssetBrowser.OpenTo( asset ) )
			.Enabled = asset is not null;

		menu.AddOption( "Clear", "backspace", () => Assign( null ) )
			.Enabled = !IsReadOnly && path is not null;
	}

	/// <summary>The path, so it pastes into any other field that takes one.</summary>
	public override string ToClipboardString() => CurrentAsset()?.RelativePath ?? CurrentPath();

	public override void FromClipboardString( string clipboard )
		=> AssignAsset( AssetSystem.FindByPath( clipboard?.Trim() ) );

	//What the field should show for the path the property holds.
	ResourceDisplayState BuildDisplay()
	{
		var type = AssetType.ImageFile;

		if ( Property is null )
			return new( ResourceFieldMode.Placeholder, "", "", PickerName, type, null, null );

		if ( Property.IsMultipleDifferentValues )
			return new( ResourceFieldMode.Multiple, "Multiple Values", "", "", type, null, null );

		var path = Property.GetValue<string>( null );
		var asset = path is not null ? AssetSystem.FindByPath( path ) : null;

		if ( asset is not null && !asset.IsDeleted )
			return new( ResourceFieldMode.Set, asset.Name, asset.RelativePath, "", type, asset, null );

		if ( !string.IsNullOrWhiteSpace( path ) )
		{
			if ( !Package.TryParseIdent( path, out _ ) )
				return new( ResourceFieldMode.Missing, $"Missing {PickerName}", path, "", type, null, null );

			var title = Package.TryGetCached( path, out var package )
				? $"{package.Title} ☁️"
				: $"Cloud {PickerName}";

			return new( ResourceFieldMode.Set, title, path, "", type, null, null );
		}

		return new( ResourceFieldMode.Placeholder, "", "", PickerName, type, null, null );
	}

	void RefreshDisplay()
	{
		var state = BuildDisplay();
		var hash = HashCode.Combine( state.Mode, state.Title, state.Path, state.Placeholder, MultipleRows );

		if ( hash == _displayHash ) return;

		_displayHash = hash;
		_display = state;
		_field.Update( state );
	}

	void OnFieldMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mouse ) return;
		if ( mouse.MouseButton != MouseButtons.Left ) return;

		_pressPending = true;
		_dragStarted = false;
		_pressStart = Application.CursorPosition;
	}

	void OnFieldClick( PanelEvent e )
	{
		if ( e is not MousePanelEvent mouse || mouse.MouseButton != MouseButtons.Left ) return;
		if ( IsReadOnly || _dragStarted ) return;

		_pressPending = false;
		OpenPicker();
		e.StopPropagation();
	}

	void UpdateDrag()
	{
		if ( !_pressPending || _dragStarted ) return;

		if ( ( Application.MouseButtons & MouseButtons.Left ) == 0 )
		{
			_pressPending = false;
			return;
		}

		if ( Application.CursorPosition.Distance( _pressStart ) > 4 )
			TryStartDrag();
	}

	void TryStartDrag()
	{
		_dragStarted = true;
		_pressPending = false;

		if ( _display.Asset is not { } asset ) return;

		var path = asset.AbsolutePath.Replace( '\\', '/' );
		var data = new DragData
		{
			Object = asset,
			Text = $"file:///{path}",
			Url = new Uri( $"file:///{path}" )
		};

		EditorDrag.Begin( this, data, null );
	}

	public override bool AcceptEditorDrops => !IsReadOnly;

	public override DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accept = IsImage( AssetFrom( ev ) );
		SetClass( "drop-target", accept );
		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnEditorDragLeave() => SetClass( "drop-target", false );

	public override void OnEditorDragDrop( Widget.DragEvent ev )
	{
		if ( IsReadOnly ) return;

		var asset = AssetFrom( ev );
		if ( !IsImage( asset ) ) return;

		Assign( asset.RelativePath );
		ev.Action = DropAction.Link;
	}

	static Asset AssetFrom( Widget.DragEvent ev ) =>
		ev.Data.HasFileOrFolder ? AssetSystem.FindByPath( ev.Data.FileOrFolder ) : null;

	static bool IsImage( Asset asset ) =>
		asset is not null && (asset.AssetType == AssetType.ImageFile || asset.AssetType == AssetType.Texture);

	void OpenPicker()
	{
		if ( Property is null || IsReadOnly ) return;

		AssetPicker.CloseOpenPickers();

		//A picture is the usual answer and a compiled texture the other, and either may be absent - whichever exists has to lead the picker.
		var kinds = new[] { AssetType.ImageFile, AssetType.Texture }
			.Where( x => x is not null )
			.ToList();

		var path = Property.GetValue<string>( null );
		var asset = path is not null ? AssetSystem.FindByPath( path ) : null;

		var picker = AssetPicker.Create( PickerParent(), kinds.FirstOrDefault(), new AssetPicker.PickerOptions { AdditionalTypes = kinds } );
		picker.Title = "Select Image File";
		picker.SetSelection( path );
		picker.OnAssetPicked = x => AssignAsset( x.FirstOrDefault() );
		picker.Show();
		picker.SetSelection( asset );
	}

	static Widget PickerParent() => Rinten.Internal.GlobalToolsNamespace.EditorWindow;

	string CurrentPath() => Property?.GetValue<string>( null );

	Asset CurrentAsset()
	{
		var path = CurrentPath();
		return path is null ? null : AssetSystem.FindByPath( path );
	}

	void AssignAsset( Asset asset )
	{
		if ( asset is null ) return;

		Assign( asset.RelativePath );
	}

	void Assign( string path )
	{
		if ( IsReadOnly || Property is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( path );
		Property.Parent?.NoteFinishEdit( Property );
	}
}
