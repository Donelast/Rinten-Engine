namespace Editor.Inspectors;

using Editor.TreeBrowser;
using Rinten.UI;

/// <summary>An asset named by its path, for a string property carrying one of the <see cref="AssetPathAttribute"/> family - <see cref="ImageAssetPathAttribute"/> and the FGD types. The attribute names the kind of asset the field takes.</summary>
[StyleSheet( "ResourceControl/ResourceControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AssetPathAttribute )] )]
public sealed class AssetPathControl : BaseControl
{
	//What the field calls its contents where the attribute named no kind we know.
	const string UnknownName = "Unknown Resource";

	readonly ResourceFieldPanel _field;

	AssetType _assetType;
	ResourceDisplayState _display;
	int _displayHash;
	bool _pressPending;
	bool _dragStarted;
	Vector2 _pressStart;

	public override bool SupportsMultiEdit => true;

	string PickerName => _assetType?.FriendlyName ?? UnknownName;

	public AssetPathControl()
	{

		_field = AddChild<ResourceFieldPanel>();
		_field.AddEventListener( "onmousedown", OnFieldMouseDown );
		_field.AddEventListener( "onclick", OnFieldClick );
	}

	public override void Rebuild()
	{
		_assetType = ResolveAssetType();

		_displayHash = 0;
		RefreshDisplay();
	}

	//Asset type named by the attribute, matched against every extension a type accepts
	AssetType ResolveAssetType()
	{
		if ( Property is null || !Property.TryGetAttribute<AssetPathAttribute>( out var attribute ) )
			return null;

		var extension = attribute.AssetTypeExtension?.Replace( "resource:", "" ).Trim().ToLowerInvariant();

		//The FGD types name things like "model_bone", which is not a file kind at all.
		return string.IsNullOrEmpty( extension ) ? null : AssetType.FromExtension( extension );
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
			.Enabled = !IsReadOnly && !string.IsNullOrEmpty( path );
	}

	/// <summary>The path, so it pastes into any other field that takes one.</summary>
	public override string ToClipboardString() => CurrentAsset()?.RelativePath ?? CurrentPath();

	public override void FromClipboardString( string clipboard )
	{
		var asset = AssetSystem.FindByPath( clipboard?.Trim() );
		if ( !CanAssign( asset ) ) return;

		AssignAsset( asset );
	}

	//What the field should show for the path the property holds.
	ResourceDisplayState BuildDisplay()
	{
		if ( Property is null )
			return new( ResourceFieldMode.Placeholder, "", "", PickerName, _assetType, null, null );

		if ( Property.IsMultipleDifferentValues )
			return new( ResourceFieldMode.Multiple, "Multiple Values", "", "", _assetType, null, null );

		var path = Property.GetValue<string>( null );
		var asset = string.IsNullOrWhiteSpace( path ) ? null : AssetSystem.FindByPath( path );

		if ( asset is not null && !asset.IsDeleted )
			return new( ResourceFieldMode.Set, asset.Name, asset.RelativePath, "", _assetType, asset, null );

		if ( !string.IsNullOrWhiteSpace( path ) )
		{
			//Not an asset here and not something the cloud could hand us either.
			if ( !Package.TryParseIdent( path, out _ ) )
				return new( ResourceFieldMode.Missing, $"Missing {PickerName}", path, "", _assetType, null, null );

			var title = Package.TryGetCached( path, out var package )
				? $"{package.Title} ☁️"
				: $"Cloud {PickerName}";

			return new( ResourceFieldMode.Set, title, path, "", _assetType, null, null );
		}

		return new( ResourceFieldMode.Placeholder, "", "", PickerName, _assetType, null, null );
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
		var accept = CanAssign( AssetFrom( ev ) );
		SetClass( "drop-target", accept );
		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnEditorDragLeave() => SetClass( "drop-target", false );

	public override void OnEditorDragDrop( Widget.DragEvent ev )
	{
		if ( IsReadOnly ) return;

		var asset = AssetFrom( ev );
		if ( !CanAssign( asset ) ) return;

		AssignAsset( asset );
		ev.Action = DropAction.Link;
	}

	//What is being carried, found the way the resource field finds it.
	static Asset AssetFrom( Widget.DragEvent ev ) => ResourceAssignment.AssetFrom( ev.Data );

	//A field that knows no kind cannot say whether an asset belongs in it, so it takes none.
	bool CanAssign( Asset asset ) =>
		asset is not null && _assetType is not null && asset.AssetType == _assetType;

	void OpenPicker()
	{
		if ( Property is null || IsReadOnly ) return;

		AssetPicker.CloseOpenPickers();

		var path = Property.GetValue<string>( null );
		var asset = string.IsNullOrWhiteSpace( path ) ? null : AssetSystem.FindByPath( path );

		var picker = AssetPicker.Create( PickerParent(), _assetType );
		picker.Title = $"Select {Property.DisplayName}";
		picker.SetSelection( path );

		//Only the choice writes. Highlighting used to write as well, which meant walking the list with the arrow
		//keys edited the property once per row - and closing the picker left whatever was last under the cursor.
		picker.OnAssetPicked = x => AssignAsset( x.FirstOrDefault() );
		picker.Show();

		picker.SetSelection( asset );
	}

	static Widget PickerParent() => Rinten.Internal.GlobalToolsNamespace.EditorWindow;

	string CurrentPath() => Property?.GetValue<string>( null );

	Asset CurrentAsset()
	{
		var path = CurrentPath();
		return string.IsNullOrWhiteSpace( path ) ? null : AssetSystem.FindByPath( path );
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
